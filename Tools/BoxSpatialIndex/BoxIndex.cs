namespace AddedObjectRemover;

/// <summary>
/// Boxes indexed by their AABB (not a point), so a query needs no search margin however far a box
/// lies from the item's origin. Boxes covering more than <see cref="MaxGridCellsPerBox"/> grid cells
/// are kept in a short list instead of the grid, and every candidate's box is tested against the
/// query area, so large boxes are returned only where they reach. Read-only after construction and
/// safe to query from many threads at once.
/// </summary>
public sealed class BoxIndex
{
    /// <summary>64 cells of 512 units: a box wider than about one exterior cell in both directions.</summary>
    public const int MaxGridCellsPerBox = 64;

    private readonly Box[] _boxes;
    private readonly SpatialGrid _grid;
    private readonly int[] _gridMembers;
    private readonly int[] _largeBoxes;

    private BoxIndex(Box[] boxes, SpatialGrid grid, int[] gridMembers, int[] largeBoxes)
    {
        _boxes = boxes;
        _grid = grid;
        _gridMembers = gridMembers;
        _largeBoxes = largeBoxes;
    }

    public int LargeBoxCount => _largeBoxes.Length;

    /// <param name="boxes">Box i belongs to slot i.</param>
    public static BoxIndex Build(Box[] boxes)
    {
        var gridMembers = new List<int>();
        var largeBoxes = new List<int>();
        for (var i = 0; i < boxes.Length; i++)
        {
            (SpatialGrid.CountCells(boxes[i]) > MaxGridCellsPerBox ? largeBoxes : gridMembers).Add(i);
        }
        var grid = SpatialGrid.FromBoxes(gridMembers.Select(i => boxes[i]).ToArray());
        return new BoxIndex(boxes, grid, gridMembers.ToArray(), largeBoxes.ToArray());
    }

    /// <summary>Replaces <paramref name="candidates"/> with the ascending, distinct slots of the boxes that overlap <paramref name="area"/>.</summary>
    /// <param name="slots">Reused buffer.</param>
    public void CollectOverlapping(Box area, List<int> slots, List<int> candidates)
    {
        slots.Clear();
        _grid.Collect(area, slots);
        candidates.Clear();
        foreach (var slot in slots) AddIfOverlapping(_gridMembers[slot], area, candidates);
        foreach (var large in _largeBoxes) AddIfOverlapping(large, area, candidates);
        candidates.Sort();
        SpatialGrid.RemoveAdjacentDuplicates(candidates);
    }

    private void AddIfOverlapping(int slot, Box area, List<int> candidates)
    {
        if (_boxes[slot].Overlaps(area)) candidates.Add(slot);
    }
}
