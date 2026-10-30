using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <param name="Evaluations">In target order, each object's newest check: an object a round kept as referenced is <see cref="VisibleLeftBehindOutcome.KeptReferenced"/> with its keep reason, and one a round removed as a linked member is <see cref="VisibleLeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="VisibleLeftBehindOutcome.RemovedLinkedToRestoration"/>; an object a round restored has the matching restored outcome. An object an also-remove follow-up decided keeps its check's verdict; <paramref name="FollowUp"/> reports that decision.</param>
/// <param name="ProposedRounds">The visible left-behind rounds and their also-remove follow-up rounds, in the order to apply them; empty unless the step was performed.</param>
/// <param name="Rounds">The visible left-behind rounds as the decisions number them once <paramref name="ProposedRounds"/> are applied; empty unless the step was performed.</param>
/// <param name="RoundsTime">The time of the visible left-behind rounds alone: their judging and deciding, not their also-remove follow-ups or the measuring before the first round.</param>
/// <param name="FollowUp">The also-remove follow-up rounds of every visible left-behind round as one result; without rounds when nothing was followed up.</param>
/// <param name="Failure">The failure the step stopped with; null unless <see cref="Outcome"/> is <see cref="StepOutcome.StoppedByUnexpectedError"/>.</param>
public sealed record VisibleLeftBehindResult(
    StepOutcome Outcome,
    IReadOnlyList<VisibleLeftBehindCheck> Evaluations,
    ImmutableArray<ProposedRound> ProposedRounds,
    ImmutableArray<Round> Rounds,
    TimeSpan RoundsTime,
    RestingObjectsResult FollowUp,
    PartFailure? Failure);
