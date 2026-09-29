using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// EverythingTouching: an undecided object whose mesh touches, within the touch gap, a mesh removed
/// in the previous round is proposed for removal, attributed to the first frontier object (in
/// target order, then neighbour order) that touches it. Held objects never spread, because the
/// ledger never lists them as removed.
/// </summary>
/// <param name="search">Built once from the seeds; its participants exclude the objects held in the too-close round.</param>
internal sealed class TouchRule(TouchSearch search, ParallelOptions parallelOptions, IPhaseTimer timer) : IFollowUpRule
{
    public RoundProposals Next(Ledger ledger, ImmutableArray<TargetId> removedLastRound)
    {
        var frontier = removedLastRound.Order().Select(target => target.Index).ToList();
        var pairs = timer.Time(
            TimedPhase.TouchBroadPhase, () => search.CollectFrontierPairs(frontier, skip: node => ledger.IsDecided(new TargetId(node))));
        var (reached, work) = timer.Time(
            TimedPhase.TouchNarrowPhase, () => search.Tester.FindFirstInContact(pairs, ContactRule.Touch, parallelOptions));
        return new RoundProposals(
            [.. reached.Select(pair => new Proposal(new TargetId(pair.Second), new Cause.Touching(new TargetId(pair.First))))],
            new TouchRound([.. reached], work));
    }
}
