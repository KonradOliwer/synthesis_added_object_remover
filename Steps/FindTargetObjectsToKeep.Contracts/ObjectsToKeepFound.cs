namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>What the step found, with how long each of its two parts took.</summary>
/// <param name="HeldUp">The target objects other mods' objects rest on, found first.</param>
/// <param name="HeldUpTime">Finding <paramref name="HeldUp"/>.</param>
/// <param name="Kept">The objects to keep, from the links, the scripts and <paramref name="HeldUp"/>.</param>
/// <param name="KeepTime">Finding <paramref name="Kept"/>.</param>
public sealed record ObjectsToKeepFound(HeldUpTargets HeldUp, TimeSpan HeldUpTime, ObjectsToKeepResult Kept, TimeSpan KeepTime);
