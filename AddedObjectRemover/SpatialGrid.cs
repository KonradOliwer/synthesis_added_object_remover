using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Exact test run by <see cref="SpatialGrid.TryFindFirst{TMatcher}"/> on each candidate index.</summary>
internal interface IGridMatcher
{
    bool IsMatch(int index);
}

/// <summary>
/// Immutable uniform 2D (X/Y) spatial hash of item indices. Z is not bucketed; callers run an
/// exact test on every candidate anyway, so the grid only has to be a conservative pre-filter.
///
/// Items are stored per grid cell in one flat index array (cell order, then insertion order),
/// so a query touches no per-item objects and allocates nothing. The grid is read-only after
/// construction and therefore safe to query from many threads at once.
/// </summary>
internal sealed class SpatialGrid
{
    /// <summary>Edge length of one grid cell in game units (an exterior cell is 4096 units wide).</summary>
    public const float CellSize = 512f;

    /// <summary>
    /// A box item covering more grid cells than this is not bucketed but kept in a short list that
    /// every query checks, so one huge (or broken) box cannot blow up the grid.
    /// </summary>
    private const long MaxCellsPerItem = 1024;

    private readonly Dictionary<long, int> _slotByCell;
    private readonly int[] _slotStart; // slot -> start offset in _items; length = slots + 1
    private readonly int[] _items;
    private readonly int[] _oversize;

    public int Count { get; }

    private SpatialGrid(int count, Dictionary<long, int> slotByCell, int[] slotStart, int[] items, int[] oversize)
    {
        Count = count;
        _slotByCell = slotByCell;
        _slotStart = slotStart;
        _items = items;
        _oversize = oversize;
    }

    /// <summary>Indexes each point (item index = position in the list) in the one cell containing it.</summary>
    public static SpatialGrid FromPoints(IReadOnlyList<Vector3> points)
    {
        var boxes = new Box[points.Count];
        for (var i = 0; i < boxes.Length; i++) boxes[i] = new Box(points[i], points[i]);
        return FromBoxes(boxes);
    }

    /// <summary>Indexes each box (item index = position in the list) in every cell its X/Y range overlaps.</summary>
    public static SpatialGrid FromBoxes(IReadOnlyList<Box> boxes)
    {
        var slotByCell = new Dictionary<long, int>();
        var counts = new List<int>();
        var oversize = new List<int>();

        // Pass 1: assign slots (first-seen order) and count entries per slot.
        for (var i = 0; i < boxes.Count; i++)
        {
            if (!TryGetCellRange(boxes[i], out var x0, out var x1, out var y0, out var y1))
            {
                oversize.Add(i);
                continue;
            }
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    var key = Pack(x, y);
                    if (!slotByCell.TryGetValue(key, out var slot))
                    {
                        slot = counts.Count;
                        slotByCell[key] = slot;
                        counts.Add(0);
                    }
                    counts[slot]++;
                }
            }
        }

        var slotStart = new int[counts.Count + 1];
        for (var s = 0; s < counts.Count; s++) slotStart[s + 1] = slotStart[s] + counts[s];

        // Pass 2: fill, keeping insertion order within each cell.
        var items = new int[slotStart[counts.Count]];
        var fill = new int[counts.Count];
        Array.Copy(slotStart, fill, counts.Count);
        for (var i = 0; i < boxes.Count; i++)
        {
            if (!TryGetCellRange(boxes[i], out var x0, out var x1, out var y0, out var y1)) continue;
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    items[fill[slotByCell[Pack(x, y)]]++] = i;
                }
            }
        }

        return new SpatialGrid(boxes.Count, slotByCell, slotStart, items, oversize.ToArray());
    }

    /// <summary>
    /// Finds the first item indexed in a grid cell overlapping the X/Y range of
    /// <paramref name="area"/> (grid-cell granularity) that satisfies the matcher. The matcher
    /// receives the item index, so a caller can index by a cheap approximate point (e.g. a raw
    /// position) and do an exact, possibly expensive, test only for the few candidates whose
    /// indexed cell falls in range. An item indexed in several cells may be tested more than once.
    /// </summary>
    public bool TryFindFirst<TMatcher>(Box area, ref TMatcher matcher, out int index)
        where TMatcher : struct, IGridMatcher
    {
        foreach (var candidate in _oversize)
        {
            if (matcher.IsMatch(candidate))
            {
                index = candidate;
                return true;
            }
        }

        long x0 = ToCell(area.Min.X);
        long x1 = ToCell(area.Max.X);
        long y0 = ToCell(area.Min.Y);
        long y1 = ToCell(area.Max.Y);
        var cellCount = (x1 - x0 + 1) * (y1 - y0 + 1);

        if (cellCount > _slotByCell.Count)
        {
            // Query area covers more grid cells than are occupied: scanning the occupied cells is cheaper.
            for (var slot = 0; slot < _slotStart.Length - 1; slot++)
            {
                if (TryMatchSlot(slot, ref matcher, out index)) return true;
            }
        }
        else
        {
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    if (_slotByCell.TryGetValue(Pack((int)x, (int)y), out var slot)
                        && TryMatchSlot(slot, ref matcher, out index))
                    {
                        return true;
                    }
                }
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Adds every item indexed in a grid cell overlapping the X/Y range of <paramref name="area"/>
    /// to <paramref name="results"/>. Items indexed in several cells can appear more than once.
    /// </summary>
    public void Collect(Box area, List<int> results)
    {
        results.AddRange(_oversize);

        long x0 = ToCell(area.Min.X);
        long x1 = ToCell(area.Max.X);
        long y0 = ToCell(area.Min.Y);
        long y1 = ToCell(area.Max.Y);
        var cellCount = (x1 - x0 + 1) * (y1 - y0 + 1);

        if (cellCount > _slotByCell.Count)
        {
            results.AddRange(_items);
            return;
        }

        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                if (!_slotByCell.TryGetValue(Pack((int)x, (int)y), out var slot)) continue;
                for (var i = _slotStart[slot]; i < _slotStart[slot + 1]; i++) results.Add(_items[i]);
            }
        }
    }

    private bool TryMatchSlot<TMatcher>(int slot, ref TMatcher matcher, out int index)
        where TMatcher : struct, IGridMatcher
    {
        for (var i = _slotStart[slot]; i < _slotStart[slot + 1]; i++)
        {
            var candidate = _items[i];
            if (matcher.IsMatch(candidate))
            {
                index = candidate;
                return true;
            }
        }
        index = -1;
        return false;
    }

    private static bool TryGetCellRange(Box box, out int x0, out int x1, out int y0, out int y1)
    {
        x0 = ToCell(box.Min.X);
        x1 = ToCell(box.Max.X);
        y0 = ToCell(box.Min.Y);
        y1 = ToCell(box.Max.Y);
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        return ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) <= MaxCellsPerItem;
    }

    private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

    private static int ToCell(float coordinate)
    {
        var cell = Math.Floor(coordinate / CellSize);
        if (double.IsNaN(cell)) return 0;
        return (int)Math.Clamp(cell, int.MinValue, int.MaxValue);
    }
}
