using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point lies inside a placed object: the object's mesh surrounds it
/// (<see cref="SurroundingRayTest"/>). Objects without readable mesh triangles contain nothing. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(BaseObjectShapeProvider shapes, TriangleTreeCache meshCache)
{
    [ThreadStatic] private static List<int>? _scratch;

    private readonly struct ContainingMatcher(OtherObjectIndex index, ObjectContainment containment, Vector3 point, bool skipReplaced)
        : IGridMatcher
    {
        /// <remarks>The reach test comes first: it is cheap, and visibility may need the object's mesh measured.</remarks>
        public bool IsMatch(int otherIndex)
        {
            if (skipReplaced && index.IsReplaced(otherIndex)) return false;
            var other = index[otherIndex];
            return containment.CanReach(other, point)
                && index.IsVisible(otherIndex)
                && containment.Contains(other.Base, other.Transform, point);
        }
    }

    public bool Contains(BaseRef? baseRef, PlacedTransform transform, Vector3 worldPoint)
    {
        if (shapes.GetMeshPath(baseRef) is not { } meshPath) return false;
        var local = transform.ToLocal(worldPoint);
        if (!shapes.GetLocalBox(baseRef).Contains(local)) return false;

        using var lease = meshCache.Acquire(meshPath);
        return lease.Tree is { } tree && SurroundingRayTest.IsSurrounded(tree, local, transform.Rotation, _scratch ??= []);
    }

    /// <param name="skipReplaced">Ignore objects the target plugin replaced.</param>
    /// <returns>Index of the first visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(OtherObjectIndex index, Vector3 worldPoint, bool skipReplaced)
    {
        var area = new Box(worldPoint, worldPoint).Grown(OtherObjectIndex.RawPositionSearchMargin);
        var matcher = new ContainingMatcher(index, this, worldPoint, skipReplaced);
        return index.Grid.TryFindFirst(area, ref matcher, out var match) ? match : -1;
    }

    /// <summary>Whether the object's bounding box, however it is rotated, can reach the point.</summary>
    private bool CanReach(OtherObject other, Vector3 worldPoint)
    {
        var reach = shapes.GetLocalBox(other.Base).FarthestCornerDistance * other.Scale;
        return Vector3.DistanceSquared(other.Position, worldPoint) <= reach * reach;
    }
}
