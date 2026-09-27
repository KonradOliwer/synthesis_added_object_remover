using System.Diagnostics;

namespace AddedObjectRemover;

internal enum SupportCategory { RemovedTarget, KeptTarget, OtherPlugin, Terrain }

internal readonly record struct SupporterShare(Supporter Supporter, SupportCategory Category, float Share);

/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Removed">Removed for its own lost support.</param>
/// <param name="RemovedAsLinked">Not removed for its own support, but in the same iteration with a linked object that was.</param>
internal sealed record AnchoringEvaluation(
    int TargetIndex,
    int Iteration,
    CandidateContacts Contacts,
    IReadOnlyList<SupporterShare> Shares,
    bool Removed,
    bool RemovedAsLinked)
{
    public float RemovedShare => ShareOf(SupportCategory.RemovedTarget);

    public float ShareOf(SupportCategory category) =>
        Shares.Where(share => share.Category == category).Sum(share => share.Share);
}

/// <param name="Candidates">Distinct candidates evaluated at least once.</param>
/// <param name="KeptWithoutContacts">Candidates kept because no surface sample touched any supporter.</param>
internal sealed record AnchoringStats(
    int Iterations,
    int Candidates,
    int Evaluations,
    int KeptWithoutContacts,
    PairTestStats Pairs,
    TimeSpan Setup,
    TimeSpan TouchSearch,
    TimeSpan ContactPoints);

/// <param name="Removals">Anchoring removals and the linked group members removed with them.</param>
internal sealed record AnchoringResult(
    List<Removal> Removals,
    List<KeptTarget> Kept,
    AnchoringStats Stats,
    List<AnchoringEvaluation> Evaluations);

/// <summary>
/// Anchoring follow-up removal. Candidates are target objects with a mesh that touch an object
/// removed in the previous iteration (at first the too-close removals), or whose mesh centre area
/// that object's mesh encloses: support also counts enclosed samples, so an object buried inside a
/// removed one must become a candidate although no surfaces come close. Each candidate's support
/// is split among its supporters by weighted contact points (<see cref="AnchoringContactFinder"/>),
/// and it is removed when the share held by removed target objects reaches the threshold. A removed
/// candidate takes the rest of its linked group along. Newly removed objects, linked ones included,
/// start the next iteration, which re-evaluates every object in contact with them, until nothing
/// changes. All candidates of one iteration are judged against the removals of earlier iterations
/// only, so the result does not depend on their order. Referenced objects are never removed and
/// keep supporting others.
/// </summary>
internal sealed class AnchoringRemover
{
    /// <summary>Shares are sums of float fractions, so support that is fully removed can add up to slightly less than 1.</summary>
    private const float ShareRoundingTolerance = 1e-5f;

    private readonly LinkedGroups _groups;
    private readonly KeepReferencedRule _keepRule;
    private readonly TouchSearch _search;
    private readonly AnchoringContactFinder _contactFinder;
    private readonly float _threshold;
    private readonly ParallelOptions _parallelOptions;

    private readonly bool[] _removed;
    private readonly bool[] _keptLogged;
    private readonly bool[] _countedWithoutContacts;
    private readonly CandidateContacts?[] _contacts;

    private readonly List<Removal> _removals = [];
    private readonly List<KeptTarget> _kept = [];
    private readonly List<AnchoringEvaluation> _evaluations = [];
    private int _iterations;
    private int _keptWithoutContacts;
    private TimeSpan _touchSearch;
    private TimeSpan _contactPoints;

    private AnchoringRemover(
        int targetCount,
        LinkedGroups groups,
        KeepReferencedRule keepRule,
        TouchSearch search,
        AnchoringContactFinder contactFinder,
        float threshold,
        ParallelOptions parallelOptions)
    {
        _groups = groups;
        _keepRule = keepRule;
        _search = search;
        _contactFinder = contactFinder;
        _threshold = threshold;
        _parallelOptions = parallelOptions;
        _removed = new bool[targetCount];
        _keptLogged = new bool[targetCount];
        _countedWithoutContacts = new bool[targetCount];
        _contacts = new CandidateContacts?[targetCount];
    }

    /// <param name="visibility">Parallel to <paramref name="targets"/>; invisible targets are never candidates or supporters.</param>
    /// <param name="seeds">Target indices removed before this step; every linked group member of a seed is a seed too.</param>
    /// <param name="keptTooClose">Targets never removed that still support others, not reported again.</param>
    /// <param name="threshold">Fraction of support held by removed objects at which a candidate is removed.</param>
    public static AnchoringResult Run(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        LinkedGroups groups,
        SupporterIndex supporters,
        TerrainHeights terrain,
        BaseObjectShapeProvider shapes,
        TriangleTreeCache meshCache,
        KeepReferencedRule keepRule,
        float touchDistance,
        float threshold,
        ParallelOptions parallelOptions)
    {
        var (remover, setup) = Timing.Measure(() =>
        {
            var search = TouchSearch.Create(
                targets, visibility, groups.CollectReachableSpaces(targets, seeds), excluded: [], shapes, meshCache, touchDistance, parallelOptions);
            var supporterFinder = new AnchoringSupporterFinder(targets, search, supporters, shapes, touchDistance);
            var contactFinder = new AnchoringContactFinder(targets, search.MeshPaths, supporterFinder, terrain, search.Cache, touchDistance);
            return new AnchoringRemover(targets.Count, groups, keepRule, search, contactFinder, threshold, parallelOptions);
        });

        remover.RemoveUnanchored(seeds, keptTooClose);
        return remover.CreateResult(setup);
    }

    private void RemoveUnanchored(IReadOnlyList<int> seeds, IReadOnlyList<int> keptTooClose)
    {
        foreach (var seed in seeds) _removed[seed] = true;
        foreach (var kept in keptTooClose) _keptLogged[kept] = true;

        var frontier = seeds.ToList();
        while (frontier.Count > 0)
        {
            _iterations++;
            var (candidates, touchSearch) = Timing.Measure(() => FindCandidatesInContactWith(frontier));
            _touchSearch += touchSearch;
            _contactPoints += Timing.Measure(() => FindMissingContacts(candidates));
            frontier = EvaluateAndRemove(candidates);
        }
    }

    /// <returns>Sorted indices of objects not yet removed in contact with a frontier object, referenced objects excluded (and logged once).</returns>
    private List<int> FindCandidatesInContactWith(List<int> frontier)
    {
        var pairs = _search.CollectFrontierPairs(frontier, skip: target => _removed[target]);
        var inContact = _search.Tester.FindFirstInContact(pairs, ContactRule.TouchOrEnclose, _parallelOptions);
        return ExcludeKept(inContact);
    }

    /// <returns>Sorted second members of the pairs that are not referenced.</returns>
    private List<int> ExcludeKept(List<TargetPair> pairs)
    {
        var candidates = new SortedSet<int>();
        foreach (var (from, to) in pairs)
        {
            if (_keepRule.TryGetKeepReason(to, out var reason))
            {
                LogKeptOnce(to, reason, from);
                continue;
            }
            candidates.Add(to);
        }
        return candidates.ToList();
    }

    private void LogKeptOnce(int target, KeepReason reason, int touchedTarget)
    {
        if (_keptLogged[target]) return;
        _keptLogged[target] = true;
        _kept.Add(new KeptTarget(target, reason, touchedTarget));
    }

    /// <remarks>Contact points do not depend on what is removed, so each candidate's are found once.</remarks>
    private void FindMissingContacts(List<int> candidates)
    {
        var missing = candidates.Where(candidate => _contacts[candidate] == null).ToList();
        Parallel.ForEach(
            missing,
            _parallelOptions,
            () => new SpatialQueryScratch(),
            (candidate, _, scratch) =>
            {
                _contacts[candidate] = _contactFinder.FindContacts(candidate, scratch);
                return scratch;
            },
            _ => { });
    }

    /// <returns>The objects removed in this iteration: the removed candidates, then their linked group members.</returns>
    private List<int> EvaluateAndRemove(List<int> candidates)
    {
        var evaluations = candidates.Select(Evaluate).ToList();
        var removedCandidates = new List<int>();
        foreach (var evaluation in evaluations)
        {
            if (evaluation.Contacts.ContactPoints == 0) CountWithoutContactsOnce(evaluation.TargetIndex);
            if (!evaluation.Removed) continue;

            _removed[evaluation.TargetIndex] = true;
            _removals.Add(new AnchoringRemoval(evaluation.TargetIndex, evaluation.RemovedShare, MainRemovedSupporter(evaluation)));
            removedCandidates.Add(evaluation.TargetIndex);
        }
        var linked = removedCandidates.SelectMany(RemoveLinkedPartners).ToList();
        _evaluations.AddRange(evaluations.Select(evaluation =>
            evaluation with { RemovedAsLinked = !evaluation.Removed && _removed[evaluation.TargetIndex] }));
        return [.. removedCandidates, .. linked];
    }

    /// <returns>The members of the removed candidate's linked group removed with it.</returns>
    /// <remarks>A group shares its keep reason, so the members of a removed candidate's group are never kept.</remarks>
    private List<int> RemoveLinkedPartners(int removed)
    {
        var partners = new List<int>();
        foreach (var partner in _groups.MembersOf(removed))
        {
            if (_removed[partner]) continue;
            _removed[partner] = true;
            _removals.Add(new LinkedRemoval(partner, LinkedToTargetIndex: removed));
            partners.Add(partner);
        }
        return partners;
    }

    /// <remarks>A candidate without contact points has no shares, so it is never removed.</remarks>
    private AnchoringEvaluation Evaluate(int candidate)
    {
        var contacts = _contacts[candidate]!;
        var shares = contacts.Supporters
            .Select(entry => new SupporterShare(entry.Supporter, Categorize(entry.Supporter), entry.Weight / contacts.TotalWeight))
            .OrderByDescending(share => share.Share)
            .ThenBy(share => share.Supporter.Type)
            .ThenBy(share => share.Supporter.Index)
            .ToList();
        var evaluation = new AnchoringEvaluation(candidate, _iterations, contacts, shares, Removed: false, RemovedAsLinked: false);
        return evaluation with { Removed = ReachesThreshold(evaluation.RemovedShare, _threshold) };
    }

    internal static bool ReachesThreshold(float removedShare, float threshold) =>
        removedShare >= threshold - ShareRoundingTolerance;

    private SupportCategory Categorize(Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => _removed[supporter.Index] ? SupportCategory.RemovedTarget : SupportCategory.KeptTarget,
        SupporterType.PlacedObject => SupportCategory.OtherPlugin,
        SupporterType.Terrain => SupportCategory.Terrain,
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };

    /// <remarks>Shares are sorted largest first, and a removed candidate always has a removed supporter.</remarks>
    private static int MainRemovedSupporter(AnchoringEvaluation evaluation) =>
        evaluation.Shares.First(share => share.Category == SupportCategory.RemovedTarget).Supporter.Index;

    private void CountWithoutContactsOnce(int target)
    {
        if (_countedWithoutContacts[target]) return;
        _countedWithoutContacts[target] = true;
        _keptWithoutContacts++;
    }

    private AnchoringResult CreateResult(TimeSpan setup)
    {
        var stats = new AnchoringStats(
            _iterations,
            _contacts.Count(contacts => contacts != null),
            _evaluations.Count,
            _keptWithoutContacts,
            _search.Tester.GetStats(),
            setup,
            _touchSearch,
            _contactPoints);
        return new AnchoringResult(_removals, _kept, stats, _evaluations);
    }
}
