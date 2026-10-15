using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>
/// A private copy of the removal decisions, on which a step decides its own rounds with the decision list's rule.
/// The run's decisions never see what is decided on it; the step proposes those rounds instead.
/// </summary>
public interface IRemovalDecisionsCopy
{
    /// <summary>Decides one round of <paramref name="kind"/> from the proposals, with the decision list's rule.</summary>
    /// <returns>The copy's view with the new round.</returns>
    IRemovalDecisions DecideRound(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals);

    /// <summary>
    /// Decides also-remove rounds on the copy, each proposed from the objects the newest round removed (the seeds
    /// start the first), until a round removes nothing.
    /// </summary>
    /// <param name="seeds">The removals the first round starts from, as the step chooses them from the rounds already present.</param>
    /// <returns>The copy's view with the new rounds, and each round's proposals and details in round order.</returns>
    (IRemovalDecisions Decisions, ImmutableArray<RoundProposals<TDetails>> Rounds) DecideRoundsUntilNothingRemoved<TDetails>(
        IRemovalRounds<TDetails> rounds, ImmutableArray<TargetId> seeds);
}
