using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The target objects that other mods' objects rest on, directly or through other such target objects.</summary>
/// <param name="Targets">Each such target object with what rests on it.</param>
/// <param name="Failure">Set when an unexpected error stopped the search; no object is then kept for this reason.</param>
public sealed record HeldUpTargets(StepOutcome Outcome, IReadOnlyDictionary<TargetId, RestingObject> Targets, PartFailure? Failure)
{
    public static HeldUpTargets SwitchedOff { get; } = new(StepOutcome.SwitchedOff, ImmutableDictionary<TargetId, RestingObject>.Empty, Failure: null);
}
