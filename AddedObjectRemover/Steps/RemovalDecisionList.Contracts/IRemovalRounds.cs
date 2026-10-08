using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>A step's rule that proposes removals round by round, each from the objects the previous round removed.</summary>
/// <typeparam name="TDetails">What a round found on the way, kept for the step's own reports; the decision list never reads it.</typeparam>
public interface IRemovalRounds<TDetails>
{
    /// <param name="removedLastRound">The previous round's removals, linked ones included.</param>
    RoundProposals<TDetails> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound);
}

/// <summary>A rule's proposals for one round and what it found on the way.</summary>
public sealed record RoundProposals<TDetails>(IReadOnlyList<ProposedRemoval> Proposals, TDetails Details);
