using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>What the touch search and the support contacts read.</summary>
/// <param name="Terrain">Empty unless OnlyWhatLosesItsSupport or relocation is on.</param>
internal sealed record SupportReads(
    ImmutableArray<TargetObject> Targets,
    IBaseObjectShapes Shapes,
    ITriangleMeshes Triangles,
    ISurfaceSamples Surfaces,
    ITerrainHeights Terrain,
    Execution Exec)
{
    /// <param name="caches">Its shapes, triangles, surface samples and terrain are read.</param>
    public static SupportReads From(ImmutableArray<TargetObject> targets, ILoadOrderCaches caches, Execution execution) =>
        new(targets, caches.Shapes, caches.Triangles, caches.SurfaceSamples, caches.Terrain, execution);
}
