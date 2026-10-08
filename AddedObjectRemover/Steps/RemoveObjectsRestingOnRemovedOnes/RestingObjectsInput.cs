using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <param name="ObjectsOfAnyPlugin">Only for ObjectsSupportedByIt: every visible object of any plugin that can support a target.</param>
/// <param name="Terrain">Empty unless ObjectsSupportedByIt or relocation is on.</param>
internal sealed record RestingObjectsInput(
    ImmutableArray<TargetObject> Targets,
    IObjectsToKeep Protection,
    IVisibleObjectsOfAnyPlugin? ObjectsOfAnyPlugin,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    ITerrainHeights Terrain,
    Execution Exec);
