using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <param name="Groups">The seeded groups, by their lowest member.</param>
/// <param name="Evaluations">
/// Every candidate of a seeded group in target order, joined with what the step's round decided. Objects to keep are never
/// candidates, and neither is any member of a linked group that holds one, so the round never keeps an object as referenced or in a
/// deleted cell; one the round removed as a linked member is <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/> or
/// <see cref="LeftBehindOutcome.RemovedLinkedToRestoration"/>. An object is kept only as <see cref="LeftBehindOutcome.KeptProtectedType"/>.
/// </param>
/// <param name="ProposedRounds">The step's one round and the also-remove rounds that follow it up, in the order to apply them; empty unless the step was performed and proposed a removal.</param>
/// <param name="FollowUp">The also-remove follow-up of the step's round; without rounds when nothing was followed up.</param>
/// <param name="Failure">The failure the step stopped with; null unless <see cref="Outcome"/> is <see cref="StepOutcome.StoppedByUserDataFailure"/>.</param>
public sealed record WholeGroupsResult(
    StepOutcome Outcome,
    ImmutableArray<SeededGroup> Groups,
    IReadOnlyList<WholeGroupCheck> Evaluations,
    ImmutableArray<ProposedRound> ProposedRounds,
    RestingObjectsResult FollowUp,
    PartFailure? Failure);
