using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// ObjectsSupportedByIt. Candidates are the undecided target objects with a mesh that touch an
/// object removed in the previous round, or whose mesh centre area that object's mesh encloses:
/// support also counts enclosed samples, so an object buried inside a removed one must become a
/// candidate although no surfaces come close. Each candidate's support is split among its
/// supporters by weighted contact points (<see cref="AnchoringContactFinder"/>), and its removal is
/// proposed when the share held by removed target objects reaches the threshold. Protected
/// candidates are judged like any other, so the decisions hold only those that would really lose
/// their support; kept objects keep supporting others. Every candidate of a round is
/// judged against the removals of earlier rounds only, so the result does not depend on order.
/// </summary>
/// <param name="search">Built once from the seeds.</param>
internal sealed class SupportRule(
    TouchSearch search,
    AnchoringContactFinder contactFinder,
    float threshold,
    int targetCount,
    Execution execution)
    : IRemovalRounds<RoundDetails>
{
    /// <summary>Shares are sums of float fractions, so support that is fully removed can add up to slightly less than 1.</summary>
    private const float ShareRoundingTolerance = 1e-5f;

    /// <summary>Contact points do not depend on what is removed, so each candidate's are found once.</summary>
    private readonly CandidateContacts?[] _contacts = new CandidateContacts?[targetCount];

    internal static bool ReachesThreshold(float removedShare, float threshold) =>
        Tolerant.AtLeast(removedShare, threshold, ShareRoundingTolerance);

    public RoundProposals<RoundDetails> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
    {
        var (candidates, work) = FindCandidatesInContactWith(decisions, removedLastRound);
        FindMissingContacts(candidates);
        var evaluations = candidates.Select(candidate => Evaluate(decisions, candidate)).ToImmutableArray();
        return new RoundProposals<RoundDetails>(
            [.. evaluations.Where(evaluation => evaluation.Proposed).Select(ToProposal)],
            new SupportRoundDetails(evaluations, work));
    }

    /// <returns>Sorted undecided objects in contact with an object the previous round removed, and the pair tests that found them.</returns>
    private (List<int> Candidates, PairTestStats Work) FindCandidatesInContactWith(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
    {
        var (inContact, work) = search.FindUndecidedTouchingRemoved(decisions, removedLastRound, alsoWhenCentreEnclosed: true);
        return (inContact.Select(pair => pair.Second).Distinct().Order().ToList(), work);
    }

    private void FindMissingContacts(List<int> candidates)
    {
        var missing = candidates.Where(candidate => _contacts[candidate] == null).ToList();
        var found = ParallelMap.Run(
            execution,
            missing.Count,
            () => new ObjectQueryScratch(),
            (k, scratch) => contactFinder.FindContacts(missing[k], scratch),
            ParallelMap.AutomaticRangeSize);
        for (var k = 0; k < missing.Count; k++) _contacts[missing[k]] = found[k];
    }

    /// <remarks>A candidate without contact points has no shares, so it is never proposed.</remarks>
    private SupportEvaluation Evaluate(IRemovalDecisions decisions, int candidate)
    {
        var contacts = _contacts[candidate]!;
        var shares = contacts.Supporters
            .Select(entry => new SupporterShare(entry.Supporter, Categorize(decisions, entry.Supporter), entry.Weight / contacts.TotalWeight))
            .OrderByDescending(share => share.Share)
            .ThenBy(share => share.Supporter.Type)
            .ThenBy(share => share.Supporter.Index)
            .ToList();
        var evaluation = new SupportEvaluation(candidate, contacts, shares, Proposed: false);
        return evaluation with { Proposed = ReachesThreshold(evaluation.RemovedShare, threshold) };
    }

    private static SupportCategory Categorize(IRemovalDecisions decisions, Supporter supporter) => supporter.Type switch
    {
        SupporterType.Target => decisions.IsRemoved(new TargetId(supporter.Index)) ? SupportCategory.RemovedTarget : SupportCategory.KeptTarget,
        SupporterType.PlacedObject => SupportCategory.OtherPlugin,
        SupporterType.Terrain => SupportCategory.Terrain,
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };

    private static ProposedRemoval ToProposal(SupportEvaluation evaluation) => new(
        new TargetId(evaluation.TargetIndex),
        new RemovalReason.LostSupport(evaluation.RemovedShare, new TargetId(evaluation.MainRemovedSupporter)));
}
