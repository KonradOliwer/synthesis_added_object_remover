using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a point lies inside a placed object: inside its rotated, scaled bounding box and, when
/// its mesh is closed, also enclosed by the mesh itself (<see cref="PointContactTest.IsEnclosed"/>).
/// An open or unreadable mesh leaves the box test as the answer. Thread-safe.
/// </summary>
internal sealed class ObjectContainment(BaseObjectShapeProvider shapes)
{
    private readonly BaseObjectShapeProvider _shapes = shapes;
    private readonly TriangleTreeCache _trees = new(shapes.ReadGeometry);
    private readonly LazyCache<string, bool> _closedMeshes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A struct so the grid query is allocation-free and inlinable.</summary>
    private readonly struct ContainingMatcher(OtherObjectIndex index, ObjectContainment containment, Vector3 point, bool skipReplaced)
        : IGridMatcher
    {
        public bool IsMatch(int otherIndex) =>
            !(skipReplaced && index.IsReplaced(otherIndex))
            && index.IsVisible(otherIndex)
            && containment.Contains(index[otherIndex].Base, index[otherIndex].Transform, point);
    }

    public bool Contains(BaseRef? baseRef, PlacedTransform transform, Vector3 worldPoint)
    {
        var local = transform.ToLocal(worldPoint);
        if (!_shapes.GetLocalBox(baseRef).Contains(local)) return false;
        return _shapes.GetMeshPath(baseRef) is not { } meshPath || IsInsideMeshWhenClosed(meshPath, local);
    }

    /// <param name="skipReplaced">Ignore objects the target plugin replaced.</param>
    /// <returns>Index of the first visible object of <paramref name="index"/> containing the point, or -1.</returns>
    public int FindContainingVisible(OtherObjectIndex index, Vector3 worldPoint, bool skipReplaced)
    {
        var area = new Box(worldPoint, worldPoint).Grown(OtherObjectIndex.RawPositionSearchMargin);
        var matcher = new ContainingMatcher(index, this, worldPoint, skipReplaced);
        return index.Grid.TryFindFirst(area, ref matcher, out var match) ? match : -1;
    }

    private bool IsInsideMeshWhenClosed(string meshPath, Vector3 local)
    {
        using var lease = _trees.Acquire(meshPath);
        if (lease.Tree is not { } tree) return true;
        if (!_closedMeshes.GetOrCreate(meshPath, () => MeshClosure.IsClosed(tree))) return true;
        return PointContactTest.IsEnclosed(tree, local, []);
    }
}
