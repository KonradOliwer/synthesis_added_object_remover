using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Uniform 2D (X/Y) spatial hash of points with an attached payload. Z is not bucketed; callers run
/// an exact test on every candidate anyway, so the grid only has to be a conservative pre-filter.
/// </summary>
internal sealed class SpatialGrid<T>
{
    /// <summary>Edge length of one grid cell in game units (an exterior cell is 4096 units wide).</summary>
    public const float CellSize = 512f;

    private readonly Dictionary<(int X, int Y), List<int>> _cells = new();
    private readonly List<(Vector3 Point, T Item)> _entries = new();

    public int Count => _entries.Count;

    public void Add(Vector3 point, T item)
    {
        var index = _entries.Count;
        _entries.Add((point, item));
        var key = (ToCell(point.X), ToCell(point.Y));
        if (!_cells.TryGetValue(key, out var list))
        {
            list = new List<int>();
            _cells[key] = list;
        }
        list.Add(index);
    }

    /// <summary>
    /// Finds the first entry whose point lies in the X/Y range of <paramref name="area"/> (grid-cell
    /// granularity) and satisfies <paramref name="predicate"/>. The predicate receives the entry's
    /// item (not its indexed point), so a caller can index by a cheap approximate point (e.g. a raw
    /// position) and do an exact, possibly expensive, test only for the few candidates whose indexed
    /// point falls in range.
    /// </summary>
    public bool TryFindFirst(Box area, Func<T, bool> predicate, out T? item)
    {
        long x0 = ToCell(area.Min.X);
        long x1 = ToCell(area.Max.X);
        long y0 = ToCell(area.Min.Y);
        long y1 = ToCell(area.Max.Y);
        var cellCount = (x1 - x0 + 1) * (y1 - y0 + 1);

        if (cellCount > _cells.Count)
        {
            // Query area covers more grid cells than are occupied: scanning the occupied cells is cheaper.
            foreach (var list in _cells.Values)
            {
                if (TryMatch(list, predicate, out item)) return true;
            }
        }
        else
        {
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    if (_cells.TryGetValue(((int)x, (int)y), out var list)
                        && TryMatch(list, predicate, out item))
                    {
                        return true;
                    }
                }
            }
        }

        item = default;
        return false;
    }

    private bool TryMatch(List<int> indices, Func<T, bool> predicate, out T? item)
    {
        foreach (var index in indices)
        {
            var entry = _entries[index];
            if (predicate(entry.Item))
            {
                item = entry.Item;
                return true;
            }
        }
        item = default;
        return false;
    }

    private static int ToCell(float coordinate)
    {
        var cell = Math.Floor(coordinate / CellSize);
        if (double.IsNaN(cell)) return 0;
        return (int)Math.Clamp(cell, int.MinValue, int.MaxValue);
    }
}
