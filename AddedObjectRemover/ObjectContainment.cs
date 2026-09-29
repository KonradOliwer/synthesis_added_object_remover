using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point lies inside a placed object: the object's mesh surrounds it
/// (<see cref="SurroundingRayTest"/>). Objects without readable mesh triangles contain nothing. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(ShapeCatalog shapes, TriangleStore meshCache)
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

    /// <param name="include">Which objects of the index count; the others are skipped.</param>
    /// <returns>Lowest index of an included visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(OtherObjectIndex index, Vector3 worldPoint, Func<OtherObject, bool> include, SpatialQueryScratch scratch)
    {
        index.Bounds.CollectCandidates(new Box(worldPoint, worldPoint), scratch.Slots, scratch.Candidates);
        foreach (var otherIndex in scratch.Candidates)
        {
            var other = index[otherIndex];
            if (!include(other)) continue;
            if (index.IsVisible(otherIndex) && Contains(other.Base, other.Transform, worldPoint, scratch)) return otherIndex;
        }
        return -1;
    }
}
