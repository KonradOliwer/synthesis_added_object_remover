using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// EverythingTouching: an undecided object whose mesh touches, within the touch gap, a mesh removed
/// in the previous round is proposed for removal, attributed to the first frontier object (in
/// target order, then neighbour order) that touches it. Kept objects never spread, because the
/// decisions never list them as removed.
/// </summary>
/// <param name="search">Built once from the seeds; its participants exclude the objects kept in the too-close round.</param>
internal sealed class TouchRule(TouchSearch search) : IRemovalRounds<RoundDetails>
{
    public RoundProposals<RoundDetails> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
    {
        var (reached, work) = search.FindUndecidedTouchingRemoved(decisions, removedLastRound, alsoWhenCentreEnclosed: false);
        return new RoundProposals<RoundDetails>(
            [.. reached.Select(pair => new ProposedRemoval(new TargetId(pair.Second), new RemovalReason.Touching(new TargetId(pair.First))))],
            new TouchRoundDetails([.. reached], work));
    }
}
