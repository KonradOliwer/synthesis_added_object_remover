namespace AddedObjectRemover;

/// <summary>
/// The objects of one <see cref="OtherObjectIndex"/> indexed by their world AABB (not their
/// position), so a query needs no search margin. Objects whose AABB covers more than
/// <see cref="MaxGridCellsPerObject"/> grid cells are kept in a short list that every query
/// returns. Read-only after construction and safe to query from many threads at once.
/// </summary>
internal sealed class OtherObjectBoxIndex
{
    /// <summary>64 cells of 512 units: an object wider than about one exterior cell in both directions.</summary>
    public const int MaxGridCellsPerObject = 64;

    private readonly SpatialGrid _grid;
    private readonly int[] _gridMembers;
    private readonly int[] _largeObjects;

    private OtherObjectBoxIndex(SpatialGrid grid, int[] gridMembers, int[] largeObjects)
    {
        _grid = grid;
        _gridMembers = gridMembers;
        _largeObjects = largeObjects;
    }

    public int LargeObjectCount => _largeObjects.Length;

    public static OtherObjectBoxIndex Build(OtherObjectIndex others, BaseObjectShapeProvider shapes, ParallelOptions parallelOptions)
    {
        var aabbs = MeasureWorldAabbs(others, shapes, parallelOptions);
        var gridMembers = new List<int>();
        var largeObjects = new List<int>();
        for (var i = 0; i < aabbs.Length; i++)
        {
            (SpatialGrid.CountCells(aabbs[i]) > MaxGridCellsPerObject ? largeObjects : gridMembers).Add(i);
        }
        var grid = SpatialGrid.FromBoxes(gridMembers.Select(i => aabbs[i]).ToArray());
        return new OtherObjectBoxIndex(grid, gridMembers.ToArray(), largeObjects.ToArray());
    }

    /// <summary>Replaces <paramref name="candidates"/> with the sorted, distinct indices of objects whose AABB may overlap <paramref name="area"/>.</summary>
    /// <param name="slots">Reused buffer.</param>
    public void CollectCandidates(Box area, List<int> slots, List<int> candidates)
    {
        slots.Clear();
        _grid.Collect(area, slots);
        candidates.Clear();
        foreach (var slot in slots) candidates.Add(_gridMembers[slot]);
        candidates.AddRange(_largeObjects);
        candidates.Sort();
        RemoveAdjacentDuplicates(candidates);
    }

    private static Box[] MeasureWorldAabbs(OtherObjectIndex others, BaseObjectShapeProvider shapes, ParallelOptions parallelOptions)
    {
        var aabbs = new Box[others.Count];
        Parallel.For(0, others.Count, parallelOptions, i =>
        {
            var other = others[i];
            aabbs[i] = OrientedBox.FromLocal(shapes.GetLocalBox(other.Base), other.Transform).WorldAabb(0f);
        });
        return aabbs;
    }

    private static void RemoveAdjacentDuplicates(List<int> sorted)
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
