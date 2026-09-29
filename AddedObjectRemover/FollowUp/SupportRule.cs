using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

internal enum SupportCategory { RemovedTarget, KeptTarget, OtherPlugin, Terrain }

internal readonly record struct SupporterShare(Supporter Supporter, SupportCategory Category, float Share);

/// <summary>One candidate's support as a round judged it.</summary>
/// <param name="Shares">Share of each supporter, largest first; empty when the candidate has no contact points.</param>
/// <param name="Proposed">Its removed share reached the threshold, so the round proposed its removal.</param>
internal sealed record SupportEvaluation(int TargetIndex, CandidateContacts Contacts, IReadOnlyList<SupporterShare> Shares, bool Proposed)
{
    public float RemovedShare => ShareOf(SupportCategory.RemovedTarget);

    public float ShareOf(SupportCategory category) =>
        Shares.Where(share => share.Category == category).Sum(share => share.Share);

    /// <summary>Shares are sorted largest first, and a proposed candidate always has a removed supporter.</summary>
    public int MainRemovedSupporter => Shares.First(share => share.Category == SupportCategory.RemovedTarget).Supporter.Index;
}

/// <param name="Evaluations">In target order.</param>
/// <param name="Work">The pair tests that found the round's candidates.</param>
internal sealed record SupportRound(ImmutableArray<SupportEvaluation> Evaluations, PairTestStats Work) : RoundEvidence;

/// <summary>
/// ObjectsSupportedByIt. Candidates are the undecided target objects with a mesh that touch an
/// object removed in the previous round, or whose mesh centre area that object's mesh encloses:
/// support also counts enclosed samples, so an object buried inside a removed one must become a
/// candidate although no surfaces come close. Each candidate's support is split among its
/// supporters by weighted contact points (<see cref="AnchoringContactFinder"/>), and its removal is
/// proposed when the share held by removed target objects reaches the threshold. Protected
/// candidates are judged like any other, so the ledger holds only those that would really lose
/// their support; held and kept objects keep supporting others. Every candidate of a round is
/// judged against the removals of earlier rounds only, so the result does not depend on order.
/// </summary>
/// <param name="search">Built once from the seeds.</param>
internal sealed class SupportRule(
    TouchSearch search,
    AnchoringContactFinder contactFinder,
    float threshold,
    int targetCount,
    ParallelOptions parallelOptions,
    IPhaseTimer timer)
    : IFollowUpRule
{
    /// <summary>Shares are sums of float fractions, so support that is fully removed can add up to slightly less than 1.</summary>
    private const float ShareRoundingTolerance = 1e-5f;

    /// <summary>Contact points do not depend on what is removed, so each candidate's are found once.</summary>
    private readonly CandidateContacts?[] _contacts = new CandidateContacts?[targetCount];

    internal static bool ReachesThreshold(float removedShare, float threshold) =>
        removedShare >= threshold - ShareRoundingTolerance;

    public RoundProposals Next(Ledger ledger, ImmutableArray<TargetId> removedLastRound)
    {
        var (candidates, work) = timer.Time(TimedPhase.AnchoringTouchSearch, () => FindCandidatesInContactWith(ledger, removedLastRound));
        timer.Time(TimedPhase.AnchoringContactPoints, () => FindMissingContacts(candidates));
        var evaluations = candidates.Select(candidate => Evaluate(ledger, candidate)).ToImmutableArray();
        return new RoundProposals(
            [.. evaluations.Where(evaluation => evaluation.Proposed).Select(ToProposal)],
            new SupportRound(evaluations, work));
    }

    /// <returns>Sorted undecided objects in contact with an object the previous round removed, and the pair tests that found them.</returns>
    private (List<int> Candidates, PairTestStats Work) FindCandidatesInContactWith(Ledger ledger, ImmutableArray<TargetId> removedLastRound)
    {
        var frontier = removedLastRound.Order().Select(target => target.Index).ToList();
        var pairs = search.CollectFrontierPairs(frontier, skip: node => ledger.IsDecided(new TargetId(node)));
        var (inContact, work) = search.Tester.FindFirstInContact(pairs, ContactRule.TouchOrEnclose, parallelOptions);
        return (inContact.Select(pair => pair.Second).Distinct().Order().ToList(), work);
    }

    private void FindMissingContacts(List<int> candidates)
    {
        var missing = candidates.Where(candidate => _contacts[candidate] == null).ToList();
        var found = ParallelMap.Run(
            parallelOptions, missing.Count, () => new SpatialQueryScratch(), (k, scratch) => contactFinder.FindContacts(missing[k], scratch));
        for (var k = 0; k < missing.Count; k++) _contacts[missing[k]] = found[k];
    }

    /// <remarks>A candidate without contact points has no shares, so it is never proposed.</remarks>
    private SupportEvaluation Evaluate(Ledger ledger, int candidate)
    {
        var contacts = _contacts[candidate]!;
        var shares = contacts.Supporters
            .Select(entry => new SupporterShare(entry.Supporter, Categorize(ledger, entry.Supporter), entry.Weight / contacts.TotalWeight))
            .OrderByDescending(share => share.Share)
            .ThenBy(share => share.Supporter.Type)
            .ThenBy(share => share.Supporter.Index)
            .ToList();
        var evaluation = new SupportEvaluation(candidate, contacts, shares, Proposed: false);
        return evaluation with { Proposed = ReachesThreshold(evaluation.RemovedShare, threshold) };
    }

    private static SupportCategory Categorize(Ledger ledger, Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => ledger.IsRemoved(new TargetId(supporter.Index)) ? SupportCategory.RemovedTarget : SupportCategory.KeptTarget,
        SupporterType.PlacedObject => SupportCategory.OtherPlugin,
        SupporterType.Terrain => SupportCategory.Terrain,
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };

    private static Proposal ToProposal(SupportEvaluation evaluation) => new(
        new TargetId(evaluation.TargetIndex),
        new Cause.LostSupport(evaluation.RemovedShare, new TargetId(evaluation.MainRemovedSupporter)));
}
