using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal enum SupportCategory { RemovedTarget, KeptTarget, OtherPlugin, Terrain }

internal readonly record struct SupporterShare(Supporter Supporter, SupportCategory Category, float Share);

/// <summary>One evaluation of a candidate in one iteration.</summary>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
internal sealed record AnchoringEvaluation(
    int TargetIndex,
    int Iteration,
    CandidateContacts Contacts,
    IReadOnlyList<SupporterShare> Shares,
    float RemovedShare,
    bool Removed);

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

internal sealed record AnchoringResult(
    List<AnchoringRemoval> Removals,
    List<KeptTarget> Kept,
    AnchoringStats Stats,
    List<AnchoringEvaluation> Evaluations);

/// <summary>
/// Anchoring follow-up removal. Candidates are target objects with a mesh that touch an object
/// removed in the previous iteration (at first the too-close removals). Each candidate's support
/// is split among its supporters by weighted contact points (<see cref="AnchoringContactFinder"/>),
/// and it is removed when the share held by removed target objects reaches the threshold. Newly
/// removed objects start the next iteration, which re-evaluates every object touching them, until
/// nothing changes. All candidates of one iteration are judged against the removals of earlier
/// iterations only, so the result does not depend on their order. Referenced objects are never
/// removed and keep supporting others.
/// </summary>
internal sealed class AnchoringRemover
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly KeepReferencedRule _keepRule;
    private readonly TouchCandidateFinder _targetFinder;
    private readonly TouchPairTester _tester;
    private readonly AnchoringContactFinder _contactFinder;
    private readonly float _threshold;
    private readonly ParallelOptions _parallelOptions;

    private readonly bool[] _removed;
    private readonly bool[] _keptLogged;
    private readonly bool[] _countedWithoutContacts;
    private readonly CandidateContacts?[] _contacts;

    private readonly List<AnchoringRemoval> _removals = [];
    private readonly List<KeptTarget> _kept = [];
    private readonly List<AnchoringEvaluation> _evaluations = [];
    private int _iterations;
    private int _keptWithoutContacts;
    private TimeSpan _touchSearch;
    private TimeSpan _contactPoints;

    private AnchoringRemover(
        IReadOnlyList<TargetObject> targets,
        KeepReferencedRule keepRule,
        TouchCandidateFinder targetFinder,
        TouchPairTester tester,
        AnchoringContactFinder contactFinder,
        float threshold,
        ParallelOptions parallelOptions)
    {
        _targets = targets;
        _keepRule = keepRule;
        _targetFinder = targetFinder;
        _tester = tester;
        _contactFinder = contactFinder;
        _threshold = threshold;
        _parallelOptions = parallelOptions;
        _removed = new bool[targets.Count];
        _keptLogged = new bool[targets.Count];
        _countedWithoutContacts = new bool[targets.Count];
        _contacts = new CandidateContacts?[targets.Count];
    }

    /// <param name="seeds">Target indices of the too-close removals.</param>
    /// <param name="keptTooClose">Too-close targets kept as referenced: already logged, still supporters.</param>
    /// <param name="threshold">Fraction of support held by removed objects at which a candidate is removed.</param>
    public static AnchoringResult Run(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        TerrainHeights terrain,
        BaseObjectShapeProvider shapes,
        KeepReferencedRule keepRule,
        float touchDistance,
        float threshold,
        ParallelOptions parallelOptions)
    {
        var setupTimer = Stopwatch.StartNew();
        var meshPaths = targets.Select(t => shapes.GetMeshPath(t.Base)).ToArray();
        var cache = new TriangleTreeCache(shapes.ReadGeometry);
        var targetFinder = TouchCandidateFinder.Create(
            targets,
            seeds.Select(seed => targets[seed].SpaceKey).ToHashSet(),
            excluded: meshPaths.Select(path => path == null).ToArray(),
            shapes,
            touchDistance,
            parallelOptions);
        var remover = new AnchoringRemover(
            targets,
            keepRule,
            targetFinder,
            new TouchPairTester(targets, meshPaths, cache, touchDistance),
            new AnchoringContactFinder(targets, meshPaths, targetFinder, indexes, shapes, terrain, cache, touchDistance),
            threshold,
            parallelOptions);
        var setup = setupTimer.Elapsed;

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
            var candidates = Timed(() => FindCandidatesTouching(frontier), ref _touchSearch);
            Timed(() => FindMissingContacts(candidates), ref _contactPoints);
            frontier = EvaluateAndRemove(candidates);
        }
    }

    /// <returns>Sorted indices of objects not yet removed that touch a frontier object, referenced objects excluded (and logged once).</returns>
    private List<int> FindCandidatesTouching(List<int> frontier)
    {
        var neighbors = new List<int>[frontier.Count];
        Parallel.For(0, frontier.Count, _parallelOptions, i => neighbors[i] = _targetFinder.FindNeighbors(frontier[i]));
        var pairs = new List<TargetPair>();
        for (var i = 0; i < frontier.Count; i++)
        {
            foreach (var neighbor in neighbors[i])
            {
                if (!_removed[neighbor]) pairs.Add(new TargetPair(frontier[i], neighbor));
            }
        }

        var results = _tester.TestPairs(pairs, _parallelOptions);
        var candidates = new SortedSet<int>();
        for (var k = 0; k < pairs.Count; k++)
        {
            if (results[k] != PairTouch.Touching) continue;
            var (from, to) = pairs[k];
            if (_keepRule.TryGetKeepReason(_targets[to], out var reason))
            {
                LogKeptOnce(to, reason, from);
                continue;
            }
            candidates.Add(to);
        }
        return candidates.ToList();
    }

    private void LogKeptOnce(int target, string reason, int touchedTarget)
    {
        if (_keptLogged[target]) return;
        _keptLogged[target] = true;
        _kept.Add(new KeptTarget(target, reason, touchedTarget));
    }

    /// <remarks>Contact points do not depend on what is removed, so each candidate's are found once.</remarks>
    private void FindMissingContacts(List<int> candidates)
    {
        var missing = candidates.Where(candidate => _contacts[candidate] == null).ToList();
        Parallel.ForEach(missing, _parallelOptions, candidate => _contacts[candidate] = _contactFinder.FindContacts(candidate));
    }

    /// <returns>The candidates removed in this iteration.</returns>
    private List<int> EvaluateAndRemove(List<int> candidates)
    {
        var evaluations = candidates.Select(Evaluate).ToList();
        var removedNow = new List<int>();
        foreach (var evaluation in evaluations)
        {
            _evaluations.Add(evaluation);
            if (evaluation.Contacts.ContactPoints == 0) CountWithoutContactsOnce(evaluation.TargetIndex);
            if (!evaluation.Removed) continue;

            _removed[evaluation.TargetIndex] = true;
            _removals.Add(new AnchoringRemoval(evaluation.TargetIndex, evaluation.RemovedShare, MainRemovedSupporter(evaluation)));
            removedNow.Add(evaluation.TargetIndex);
        }
        return removedNow;
    }

    private AnchoringEvaluation Evaluate(int candidate)
    {
        var contacts = _contacts[candidate]!;
        if (contacts.ContactPoints == 0)
        {
            return new AnchoringEvaluation(candidate, _iterations, contacts, [], 0f, Removed: false);
        }

        var shares = contacts.Supporters
            .Select(entry => new SupporterShare(entry.Supporter, Categorize(entry.Supporter), entry.Weight / contacts.TotalWeight))
            .OrderByDescending(share => share.Share)
            .ThenBy(share => share.Supporter.Type)
            .ThenBy(share => share.Supporter.Index)
            .ToList();
        var removedShare = shares.Where(share => share.Category == SupportCategory.RemovedTarget).Sum(share => share.Share);
        return new AnchoringEvaluation(candidate, _iterations, contacts, shares, removedShare, Removed: removedShare >= _threshold);
    }

    private SupportCategory Categorize(Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => _removed[supporter.Index] ? SupportCategory.RemovedTarget : SupportCategory.KeptTarget,
        SupporterType.OtherObject => SupportCategory.OtherPlugin,
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

    private static T Timed<T>(Func<T> action, ref TimeSpan total)
    {
        var timer = Stopwatch.StartNew();
        var result = action();
        total += timer.Elapsed;
        return result;
    }

    private static void Timed(Action action, ref TimeSpan total)
    {
        var timer = Stopwatch.StartNew();
        action();
        total += timer.Elapsed;
    }

    private AnchoringResult CreateResult(TimeSpan setup)
    {
        var stats = new AnchoringStats(
            _iterations,
            _contacts.Count(contacts => contacts != null),
            _evaluations.Count,
            _keptWithoutContacts,
            _tester.GetStats(),
            setup,
            _touchSearch,
            _contactPoints);
        return new AnchoringResult(_removals, _kept, stats, _evaluations);
    }
}
