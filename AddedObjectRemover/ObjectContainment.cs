using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point lies inside a placed object: the object's mesh surrounds it
/// (<see cref="SurroundingRayTest"/>). Objects without readable mesh triangles contain nothing. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(BaseObjectShapeProvider shapes, TriangleTreeCache meshCache)
{
    [ThreadStatic] private static List<int>? _nearbyTriangles;
    [ThreadStatic] private static List<int>? _slots;
    [ThreadStatic] private static List<int>? _candidates;

    public bool Contains(BaseRef? baseRef, PlacedTransform transform, Vector3 worldPoint)
    {
        if (shapes.GetMeshPath(baseRef) is not { } meshPath) return false;
        var local = transform.ToLocal(worldPoint);
        if (!shapes.GetLocalBox(baseRef).Contains(local)) return false;

        using var lease = meshCache.Acquire(meshPath);
        return lease.Tree is { } tree && SurroundingRayTest.IsSurrounded(tree, local, transform.Rotation, _nearbyTriangles ??= []);
    }

    /// <param name="skipReplaced">Ignore objects the target plugin replaced.</param>
    /// <returns>Lowest index of a visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(OtherObjectIndex index, Vector3 worldPoint, bool skipReplaced)
    {
        var candidates = _candidates ??= [];
        index.Bounds.CollectCandidates(new Box(worldPoint, worldPoint), _slots ??= [], candidates);
        foreach (var otherIndex in candidates)
        {
            if (skipReplaced && index.IsReplaced(otherIndex)) continue;
            var other = index[otherIndex];
            if (index.IsVisible(otherIndex) && Contains(other.Base, other.Transform, worldPoint)) return otherIndex;
        }
        return -1;
    }
}
