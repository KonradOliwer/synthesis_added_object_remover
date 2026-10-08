using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A rectangle of exterior cells, the corner cells included.</summary>
public readonly record struct CellArea(int MinX, int MinY, int MaxX, int MaxY)
{
    public static CellArea Single(int x, int y) => new(x, y, x, y);

    /// <summary>The cells that hold a part of the horizontal extent of <paramref name="box"/>.</summary>
    public static CellArea Covering(Box box) => new(
        ExteriorGrid.CellIndex(box.Min.X), ExteriorGrid.CellIndex(box.Min.Y), ExteriorGrid.CellIndex(box.Max.X), ExteriorGrid.CellIndex(box.Max.Y));

    /// <summary>This area and the ring of cells around it.</summary>
    public CellArea WithNeighbors() => new(MinX - 1, MinY - 1, MaxX + 1, MaxY + 1);

    public bool Contains(Vector3 point)
    {
        var x = ExteriorGrid.CellIndex(point.X);
        var y = ExteriorGrid.CellIndex(point.Y);
        return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }

    /// <summary>The area's world rectangle shrunk by <paramref name="inset"/> on every side.</summary>
    public (Vector2 Min, Vector2 Max) GetInsetRectangle(float inset) => (
        new Vector2(MinX * ExteriorGrid.CellSize + inset, MinY * ExteriorGrid.CellSize + inset),
        new Vector2((MaxX + 1) * ExteriorGrid.CellSize - inset, (MaxY + 1) * ExteriorGrid.CellSize - inset));

    /// <summary>Whether the point lies horizontally in <see cref="GetInsetRectangle"/>.</summary>
    public bool ContainsInset(Vector3 point, float inset)
    {
        var (min, max) = GetInsetRectangle(inset);
        return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
    }

    public IEnumerable<(int X, int Y)> Cells()
    {
        for (var x = MinX; x <= MaxX; x++)
        {
            for (var y = MinY; y <= MaxY; y++) yield return (x, y);
        }
    }
}
