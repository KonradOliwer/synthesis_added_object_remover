using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <param name="Evaluations">In target order; an object the left-behind round kept as referenced is <see cref="LeftBehindOutcome.KeptReferenced"/> with its keep reason, and one the round removed as a linked member is <see cref="LeftBehindOutcome.RemovedLinkedToRemoval"/>.</param>
/// <param name="Proposals">The removals the rules propose, in target order; empty unless the step was performed.</param>
/// <param name="Failure">The failure the step stopped with; null unless <see cref="Outcome"/> is <see cref="StepOutcome.StoppedByUnexpectedError"/>.</param>
public sealed record LeftBehindResult(
    StepOutcome Outcome, IReadOnlyList<LeftBehindCheck> Evaluations, IReadOnlyList<ProposedRemoval> Proposals, PartFailure? Failure)
{
    public static LeftBehindResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], Failure: null);
}
