using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <param name="Evaluations">In target order; an object the round kept as referenced is <see cref="VisibleLeftBehindOutcome.KeptReferenced"/> with its keep reason.</param>
/// <param name="Proposals">The removals the rules propose, in target order; empty unless the step was performed.</param>
/// <param name="Failure">The failure the step stopped with; null unless <see cref="Outcome"/> is <see cref="StepOutcome.StoppedByUnexpectedError"/>.</param>
public sealed record VisibleLeftBehindResult(
    StepOutcome Outcome, IReadOnlyList<VisibleLeftBehindCheck> Evaluations, IReadOnlyList<ProposedRemoval> Proposals, PartFailure? Failure)
{
    public static VisibleLeftBehindResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], Failure: null);
}
