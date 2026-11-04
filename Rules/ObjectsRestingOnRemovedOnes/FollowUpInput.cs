using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <param name="Targets">Where they stood before any decision.</param>
/// <param name="Decisions">The decisions the rounds start from; the rounds measure the target objects where these place them.</param>
/// <param name="TimeSinceStart">The run's clock, which measures the rounds.</param>
/// <param name="Caches">
/// Its shapes, triangles, surface samples and terrain are read; its objects of any plugin are asked only when the support rounds
/// have seeds: the first ask builds the supporters' bounds index.
/// </param>
internal sealed record FollowUpInput(
    ImmutableArray<TargetObject> Targets,
    IRemovalDecisions Decisions,
    AlsoRemoveSettings Settings,
    Execution Execution,
    Func<TimeSpan> TimeSinceStart,
    IRunCaches Caches);
