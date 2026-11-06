namespace AddedObjectRemover;

/// <summary>The grid of a worldspace's exterior cells: squares 4096 game units wide, cell (0, 0) starting at the world origin.</summary>
public static class ExteriorGrid
{
    public const float CellSize = 4096f;

    public static int CellIndex(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

    /// <summary>Every cell whose square the box's footprint seen from above reaches into or touches, column by column.</summary>
    public static IEnumerable<(int X, int Y)> CellsUnder(Box box)
    {
        for (var x = CellIndex(box.Min.X); x <= CellIndex(box.Max.X); x++)
        {
            for (var y = CellIndex(box.Min.Y); y <= CellIndex(box.Max.Y); y++) yield return (x, y);
        }
    }

    /// <summary>Whether the box's footprint seen from above reaches into the cell's square, more than touching its edge.</summary>
    public static bool ReachesInto(Box box, (int X, int Y) cell) =>
        cell.X * CellSize < box.Max.X && (cell.X + 1) * CellSize > box.Min.X && cell.Y * CellSize < box.Max.Y && (cell.Y + 1) * CellSize > box.Min.Y;
}
