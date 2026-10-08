using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// Whether a point lies inside a placed object: the object's mesh surrounds it
/// (<see cref="MeshContact.PointInside"/>). Objects without readable mesh triangles contain nothing. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(IBaseObjectShapes shapes, ITriangleMeshes meshCache)
{
    /// <param name="scratch">Only its triangle buffer is used.</param>
    public bool Contains(BaseKey? baseKey, PlacedTransform transform, Vector3 worldPoint, ObjectQueryScratch scratch)
    {
        if (shapes.Of(baseKey).MeshPath is not { } meshPath) return false;
        var local = transform.ToLocal(worldPoint);
        if (!shapes.Of(baseKey).Box.Contains(local)) return false;

        using var lease = meshCache.Acquire(meshPath);
        return lease.Value is { } tree && MeshContact.PointInside(tree, local, transform.Rotation, scratch.Triangles);
    }

    /// <param name="include">Which objects of the index count; the others are skipped.</param>
    /// <returns>Lowest index of an included visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(IOtherObjectIndex index, Vector3 worldPoint, Func<OtherObject, bool> include, ObjectQueryScratch scratch)
    {
        return index.Bounds.FirstContaining(
            worldPoint,
            include: slot => include(index[slot]),
            contains: slot => index.IsVisible(slot) && Contains(index[slot].Base, index[slot].Transform, worldPoint, scratch),
            scratch.Spatial);
    }
}
