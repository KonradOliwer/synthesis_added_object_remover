using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// EverythingTouchingIt: an undecided object whose mesh touches, within the touch gap, a mesh removed
/// in the previous round is proposed for removal, attributed to the first frontier object (in
/// target order, then neighbour order) that touches it. Kept objects never spread, because the
/// decisions never list them as removed.
/// </summary>
/// <param name="search">Its participants exclude the objects kept before the rounds start.</param>
internal sealed class TouchRule(TouchSearch search) : IRemovalRounds<RoundDetails>
{
    public RoundProposals<RoundDetails> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
    {
        var (reached, work) = search.FindUndecidedTouchingRemoved(decisions, removedLastRound, alsoWhenCentreEnclosed: false);
        return new RoundProposals<RoundDetails>(
            [.. reached.Select(pair => new ProposedRemoval(new TargetId(pair.Second), new TouchingTargetThatLeft(new TargetId(pair.First))))],
            new TouchRoundDetails([.. reached], work));
    }
}
