using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point lies inside a placed object: the object's mesh surrounds it
/// (<see cref="SurroundingRayTest"/>). Objects without readable mesh triangles contain nothing. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(BaseObjectShapeProvider shapes, TriangleTreeCache meshCache)
{
    /// <param name="scratch">Only its triangle buffer is used.</param>
    public bool Contains(BaseRef? baseRef, PlacedTransform transform, Vector3 worldPoint, SpatialQueryScratch scratch)
    {
        if (shapes.GetMeshPath(baseRef) is not { } meshPath) return false;
        var local = transform.ToLocal(worldPoint);
        if (!shapes.GetLocalBox(baseRef).Contains(local)) return false;

        using var lease = meshCache.Acquire(meshPath);
        return lease.Tree is { } tree && SurroundingRayTest.IsSurrounded(tree, local, transform.Rotation, scratch.Triangles);
    }

    /// <param name="skipReplaced">Ignore objects the target plugin replaced.</param>
    /// <returns>Lowest index of a visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(OtherObjectIndex index, Vector3 worldPoint, bool skipReplaced, SpatialQueryScratch scratch)
    {
        index.Bounds.CollectCandidates(new Box(worldPoint, worldPoint), scratch.Slots, scratch.Candidates);
        foreach (var otherIndex in scratch.Candidates)
        {
            if (skipReplaced && index.IsReplaced(otherIndex)) continue;
            var other = index[otherIndex];
            if (index.IsVisible(otherIndex) && Contains(other.Base, other.Transform, worldPoint, scratch)) return otherIndex;
        }
        return -1;
    }
}
