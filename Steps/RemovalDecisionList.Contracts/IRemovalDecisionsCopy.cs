using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>
/// A private copy of the removal decisions, on which a step decides its own rounds with the decision list's rule.
/// The run's decisions never see what is decided on it; the step proposes those rounds instead.
/// </summary>
public interface IRemovalDecisionsCopy
{
    /// <summary>Decides one round, opened as <paramref name="opening"/> says, from the proposals, with the decision list's rule.</summary>
    /// <returns>The copy's view with the new round.</returns>
    IRemovalDecisions DecideRound(RoundOpening opening, IReadOnlyList<ProposedRemoval> proposals);

    /// <summary>
    /// Opens one <see cref="RoundKind.Restore"/> round that records each entry as restored to its own earlier version, or as removed
    /// when that version stands nowhere, in target order, without the when-removed table, the protection and the linked groups. A
    /// decided or protected target is a bug.
    /// </summary>
    /// <returns>The copy's view with the new round, which leaves in exactly the given targets.</returns>
    IRemovalDecisions RecordGoingBack(IReadOnlyList<GoingBack> goingBack);

    /// <summary>
    /// Decides rounds on the copy, each opened as <paramref name="opening"/> says and proposed from the objects the newest round
    /// removed (the seeds start the first), until a round removes nothing.
    /// </summary>
    /// <param name="seeds">The removals the first round starts from, as the step chooses them from the rounds already present.</param>
    /// <returns>The copy's view with the new rounds, and each round's proposals and details in round order.</returns>
    (IRemovalDecisions Decisions, ImmutableArray<RoundProposals<TDetails>> Rounds) DecideRoundsUntilNothingRemoved<TDetails>(
        RoundOpening opening, IRemovalRounds<TDetails> rounds, ImmutableArray<TargetId> seeds);
}
