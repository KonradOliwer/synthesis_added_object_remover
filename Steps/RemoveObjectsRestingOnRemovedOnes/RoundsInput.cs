using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>What the also-remove rounds read.</summary>
/// <param name="ObjectsOfAnyPlugin">Only for OnlyWhatLosesItsSupport: every visible object of any plugin that can support a target.</param>
/// <param name="Terrain">Empty unless OnlyWhatLosesItsSupport or relocation is on.</param>
internal sealed record RoundsInput(
    ImmutableArray<TargetObject> Targets,
    IVisibleObjectsOfAnyPlugin? ObjectsOfAnyPlugin,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    ISurfaceSamples Surfaces,
    ITerrainHeights Terrain,
    Execution Exec);
