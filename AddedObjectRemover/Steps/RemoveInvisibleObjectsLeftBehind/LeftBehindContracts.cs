using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <param name="Hosts">The other-mod object that can cause removals which each invisible target sits inside.</param>
/// <param name="RemovalDecisions">The decisions after the also-remove rounds; their removals clear the surroundings.</param>
/// <param name="Surroundings">The visible target objects, removed or not.</param>
internal sealed record LeftBehindInput(
    ImmutableArray<TargetObject> Targets,
    Hosts Hosts,
    IRemovalDecisions RemovalDecisions,
    IVisibleTargetObjects Surroundings,
    IBaseFacts Bases,
    IBaseObjectShapes Shapes,
    WorkOrder Order,
    Execution Exec);
