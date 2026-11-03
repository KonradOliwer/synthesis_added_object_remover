using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <param name="Evaluations">
/// Every candidate in target order, joined with what the step's round decided. Objects to keep are never candidates, so the round
/// never keeps one as referenced or in a deleted cell; one the round removed as a linked member is
/// <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/> or <see cref="LeftBehindOutcome.RemovedLinkedToRestoration"/>. An object the
/// follow-up decided keeps its check's verdict; <paramref name="FollowUp"/> reports that decision.
/// </param>
/// <param name="ProposedRounds">The step's one round and the also-remove rounds that follow it up, in the order to apply them; empty unless the step was performed and proposed a removal.</param>
/// <param name="JudgeTime">The time the candidates took to judge.</param>
/// <param name="FollowUp">The also-remove follow-up of the step's round; without rounds when nothing was followed up.</param>
/// <param name="Failure">The failure the step stopped with; null unless <see cref="Outcome"/> is <see cref="StepOutcome.StoppedByUnexpectedError"/>.</param>
public sealed record ObjectsInClearedAreasResult(
    StepOutcome Outcome,
    IReadOnlyList<ClearedAreaCheck> Evaluations,
    ImmutableArray<ProposedRound> ProposedRounds,
    TimeSpan JudgeTime,
    RestingObjectsResult FollowUp,
    PartFailure? Failure);
