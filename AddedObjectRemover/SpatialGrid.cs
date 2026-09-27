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

    private SpatialGrid(Dictionary<long, int> slotByCell, int[] slotStart, int[] items, int[] oversize)
    {
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
        var (slotByCell, counts, oversize) = AssignSlots(boxes);

        var slotStart = new int[counts.Count + 1];
        for (var s = 0; s < counts.Count; s++) slotStart[s + 1] = slotStart[s] + counts[s];

        var items = FillSlots(boxes, slotByCell, slotStart);
        return new SpatialGrid(slotByCell, slotStart, items, oversize.ToArray());
    }

    /// <summary>Slots are numbered in first-seen cell order; counts are entries per slot.</summary>
    private static (Dictionary<long, int> SlotByCell, List<int> Counts, List<int> Oversize) AssignSlots(IReadOnlyList<Box> boxes)
    {
        var slotByCell = new Dictionary<long, int>();
        var counts = new List<int>();
        var oversize = new List<int>();
        for (var i = 0; i < boxes.Count; i++)
        {
            if (!TryGetItemCellRange(boxes[i], out var x0, out var x1, out var y0, out var y1))
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
        return (slotByCell, counts, oversize);
    }

    /// <summary>Flat item array grouped by slot, keeping insertion order within each cell.</summary>
    private static int[] FillSlots(IReadOnlyList<Box> boxes, Dictionary<long, int> slotByCell, int[] slotStart)
    {
        var slotCount = slotStart.Length - 1;
        var items = new int[slotStart[slotCount]];
        var fill = new int[slotCount];
        Array.Copy(slotStart, fill, slotCount);
        for (var i = 0; i < boxes.Count; i++)
        {
            if (!TryGetItemCellRange(boxes[i], out var x0, out var x1, out var y0, out var y1)) continue;
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    items[fill[slotByCell[Pack(x, y)]]++] = i;
                }
            }
        }
        return items;
    }

    /// <summary>
    /// First item in grid cells overlapping <paramref name="area"/>'s X/Y range that the matcher
    /// accepts; items may be tested more than once.
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

        var range = CellRange.Of(area);
        if (range.CellCount > _slotByCell.Count)
        {
            // Scanning the occupied cells is cheaper than walking the query range.
            for (var slot = 0; slot < _slotStart.Length - 1; slot++)
            {
                if (TryMatchSlot(slot, ref matcher, out index)) return true;
            }
        }
        else
        {
            for (var x = range.X0; x <= range.X1; x++)
            {
                for (var y = range.Y0; y <= range.Y1; y++)
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

        var range = CellRange.Of(area);
        if (range.CellCount > _slotByCell.Count)
        {
            results.AddRange(_items);
            return;
        }

        for (var x = range.X0; x <= range.X1; x++)
        {
            for (var y = range.Y0; y <= range.Y1; y++)
            {
                if (!_slotByCell.TryGetValue(Pack((int)x, (int)y), out var slot)) continue;
                for (var i = _slotStart[slot]; i < _slotStart[slot + 1]; i++) results.Add(_items[i]);
            }
        }
    }

    /// <summary>Number of grid cells the X/Y range of <paramref name="box"/> overlaps.</summary>
    public static double CountCells(Box box) => CellRange.Of(box).CellCount;

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

    private static bool TryGetItemCellRange(Box box, out int x0, out int x1, out int y0, out int y1)
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

    /// <summary>Inclusive grid-cell range of a query area; long so that loops over a full int range terminate.</summary>
    private readonly record struct CellRange(long X0, long X1, long Y0, long Y1)
    {
        public static CellRange Of(Box area) =>
            new(ToCell(area.Min.X), ToCell(area.Max.X), ToCell(area.Min.Y), ToCell(area.Max.Y));

        /// <summary>In double so a full int range cannot overflow.</summary>
        public double CellCount => Math.Max(0, X1 - X0 + 1) * (double)Math.Max(0, Y1 - Y0 + 1);
    }
}
