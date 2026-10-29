using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>A target object that would be removed but stays because it, or a member of its linked group, is referenced.</summary>
/// <param name="TouchedTargetIndex">The target that left its place and that it touches or rests on most, if any.</param>
/// <param name="TouchedDecision">What was decided for that target, which tells whether it was removed or restored; null when there is none.</param>
public sealed record KeptObject(int TargetIndex, KeepReason Reason, int? TouchedTargetIndex, Decision? TouchedDecision = null);
