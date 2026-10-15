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
}
