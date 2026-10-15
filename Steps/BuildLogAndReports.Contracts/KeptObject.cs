using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>A target object that would be removed but stays because it, or a member of its linked group, is referenced.</summary>
/// <param name="TouchedTargetIndex">The removed target it touches or rests on most, if any.</param>
public sealed record KeptObject(int TargetIndex, KeepReason Reason, int? TouchedTargetIndex);
