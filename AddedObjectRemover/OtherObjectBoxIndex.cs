namespace AddedObjectRemover;

/// <summary>
/// Placed objects indexed by their world AABB (not their position), so a query needs no search
/// margin however far an object's bounds lie from its origin. Objects whose AABB covers more than
/// <see cref="MaxGridCellsPerObject"/> grid cells are kept in a short list instead of the grid.
/// Every candidate's AABB is tested against the query area, so large objects are returned only
/// where they reach. Read-only after construction and safe to query from many threads at once.
/// </summary>
internal sealed class OtherObjectBoxIndex
{
    /// <summary>64 cells of 512 units: an object wider than about one exterior cell in both directions.</summary>
    public const int MaxGridCellsPerObject = 64;

    /// <summary>
    /// Growth of every indexed AABB: absorbs the rounding between this AABB and the exact tests'
    /// own transforms, so the AABB filter never rejects a true hit.
    /// </summary>
    private const float RoundingSlack = 1f;

    private readonly Box[] _aabbs;
    private readonly SpatialGrid _grid;
    private readonly int[] _gridMembers;
    private readonly int[] _largeObjects;

    private OtherObjectBoxIndex(Box[] aabbs, SpatialGrid grid, int[] gridMembers, int[] largeObjects)
    {
        _aabbs = aabbs;
        _grid = grid;
        _gridMembers = gridMembers;
        _largeObjects = largeObjects;
    }

    public int LargeObjectCount => _largeObjects.Length;

    public static OtherObjectBoxIndex Build(IReadOnlyList<OtherObject> objects, BaseObjectShapeProvider shapes, ParallelOptions parallelOptions)
    {
        var aabbs = MeasureWorldAabbs(objects, shapes, parallelOptions);
        var gridMembers = new List<int>();
        var largeObjects = new List<int>();
        for (var i = 0; i < aabbs.Length; i++)
        {
            (SpatialGrid.CountCells(aabbs[i]) > MaxGridCellsPerObject ? largeObjects : gridMembers).Add(i);
        }
        var grid = SpatialGrid.FromBoxes(gridMembers.Select(i => aabbs[i]).ToArray());
        return new OtherObjectBoxIndex(aabbs, grid, gridMembers.ToArray(), largeObjects.ToArray());
    }

    /// <summary>Replaces <paramref name="candidates"/> with the ascending, distinct indices of the objects whose AABB overlaps <paramref name="area"/>.</summary>
    /// <param name="slots">Reused buffer.</param>
    public void CollectCandidates(Box area, List<int> slots, List<int> candidates)
    {
        slots.Clear();
        _grid.Collect(area, slots);
        candidates.Clear();
        foreach (var slot in slots) AddIfOverlapping(_gridMembers[slot], area, candidates);
        foreach (var large in _largeObjects) AddIfOverlapping(large, area, candidates);
        candidates.Sort();
        RemoveAdjacentDuplicates(candidates);
    }

    private void AddIfOverlapping(int index, Box area, List<int> candidates)
    {
        if (_aabbs[index].Overlaps(area)) candidates.Add(index);
    }

    private static Box[] MeasureWorldAabbs(IReadOnlyList<OtherObject> objects, BaseObjectShapeProvider shapes, ParallelOptions parallelOptions)
    {
        var aabbs = new Box[objects.Count];
        Parallel.For(0, objects.Count, parallelOptions, i =>
        {
            var other = objects[i];
            aabbs[i] = OrientedBox.FromLocal(shapes.GetLocalBox(other.Base), other.Transform).WorldAabb(RoundingSlack);
        });
        return aabbs;
    }

    public static void RemoveAdjacentDuplicates(List<int> sorted)
    {
        var kept = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (kept > 0 && sorted[kept - 1] == sorted[i]) continue;
            sorted[kept++] = sorted[i];
        }
        sorted.RemoveRange(kept, sorted.Count - kept);
    }
}
