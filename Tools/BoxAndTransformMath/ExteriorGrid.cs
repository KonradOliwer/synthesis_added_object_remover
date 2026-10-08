namespace AddedObjectRemover;

/// <summary>The grid of a worldspace's exterior cells: squares 4096 game units wide, cell (0, 0) starting at the world origin.</summary>
public static class ExteriorGrid
{
    public const float CellSize = 4096f;

    public static int CellIndex(float coordinate) => (int)MathF.Floor(coordinate / CellSize);
}
