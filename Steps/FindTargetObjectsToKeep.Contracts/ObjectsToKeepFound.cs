using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>What the step found, with how long each of its two parts took.</summary>
/// <param name="HeldUp">The target objects other mods' objects rest on, found first.</param>
/// <param name="HeldUpTime">Finding <paramref name="HeldUp"/>.</param>
/// <param name="Kept">The objects to keep, from the links, the scripts and <paramref name="HeldUp"/>.</param>
/// <param name="KeepTime">Finding <paramref name="Kept"/>.</param>
public sealed record ObjectsToKeepFound(HeldUpTargets HeldUp, TimeSpan HeldUpTime, ObjectsToKeepResult Kept, TimeSpan KeepTime);

/// <summary>The target objects that other mods' objects rest on, directly or through other such target objects.</summary>
/// <param name="Targets">Each such target object with what rests on it.</param>
/// <param name="Failure">Set when an unexpected error stopped the search; no object is then kept for this reason.</param>
public sealed record HeldUpTargets(StepOutcome Outcome, IReadOnlyDictionary<TargetId, RestingObject> Targets, PartFailure? Failure)
{
    public static HeldUpTargets SwitchedOff { get; } = new(StepOutcome.SwitchedOff, ImmutableDictionary<TargetId, RestingObject>.Empty, Failure: null);
}
