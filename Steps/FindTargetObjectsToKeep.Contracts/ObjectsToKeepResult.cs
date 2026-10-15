using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <param name="ObjectsToKeep">The target objects that are never removed, with their linked groups.</param>
/// <param name="LinkCount">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
/// <param name="CheckFailures">The target objects whose keep check failed unexpectedly, in target order; each of them is kept.</param>
public sealed record ObjectsToKeepResult(IObjectsToKeep ObjectsToKeep, int LinkCount, ImmutableArray<PartFailure> CheckFailures);
