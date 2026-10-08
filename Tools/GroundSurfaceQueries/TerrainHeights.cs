using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Terrain height of exterior cells from a height grid per cell: vertices 128 units apart covering
/// one 4096-unit cell. Between vertices the height is interpolated bilinearly. Each cell's grid is
/// read once, on first use. Thread-safe.
/// </summary>
/// <param name="readHeightGrid">
/// The heights of a cell, row by row (<see cref="VerticesPerSide"/> per row); null where the cell
/// has no terrain (no, a deleted, or an empty land record).
/// </param>
/// <param name="findLandWorldspace">
/// Target worldspace -> the worldspace whose land data it uses (itself, or a parent whose land data it uses);
/// null where that worldspace holds no land data of its own. Asked on every query, so it may start to answer only after the placed records are read.
/// </param>
public sealed class TerrainHeights(
    Func<ExteriorCell, float[]?> readHeightGrid,
    Func<RecordKey, RecordKey?> findLandWorldspace) : ITerrainHeights
{
    public const int VerticesPerSide = 33;
    private const float VertexSpacing = ExteriorGrid.CellSize / (VerticesPerSide - 1);

    private readonly ComputedOncePerKey<ExteriorCell, float[]?> _heightsByCell = new(Publication.FirstWriteWins, EqualityComparer<ExteriorCell>.Default);

    public bool HasTerrain(RecordKey spaceKey) => findLandWorldspace(spaceKey) != null;

    public bool TryGetHeight(RecordKey worldspaceKey, Vector2 position, out float height)
    {
        var cellX = ExteriorGrid.CellIndex(position.X);
        var cellY = ExteriorGrid.CellIndex(position.Y);
        if (GetHeights(worldspaceKey, cellX, cellY) is not { } heights)
        {
            height = 0;
            return false;
        }

        var local = (position - new Vector2(cellX, cellY) * ExteriorGrid.CellSize) / VertexSpacing;
        height = InterpolateBilinear(heights, local);
        return true;
    }

    public bool HasHeights(RecordKey worldspaceKey, int cellX, int cellY) => GetHeights(worldspaceKey, cellX, cellY) != null;

    private float[]? GetHeights(RecordKey worldspaceKey, int cellX, int cellY)
    {
        var landWorldspace = findLandWorldspace(worldspaceKey)
            ?? throw new InvalidOperationException($"The worldspace {worldspaceKey} has no terrain.");
        var cell = new ExteriorCell(landWorldspace, cellX, cellY);
        return _heightsByCell.Get(cell, () => readHeightGrid(cell));
    }

    /// <param name="local">Position in vertex units from the cell's south-west corner, 0 to 32 on each axis.</param>
    private static float InterpolateBilinear(float[] heights, Vector2 local)
    {
        const int lastQuad = VerticesPerSide - 2;
        var x0 = Math.Clamp((int)MathF.Floor(local.X), 0, lastQuad);
        var y0 = Math.Clamp((int)MathF.Floor(local.Y), 0, lastQuad);
        var fx = Math.Clamp(local.X - x0, 0f, 1f);
        var fy = Math.Clamp(local.Y - y0, 0f, 1f);

        float At(int x, int y) => heights[y * VerticesPerSide + x];
        var south = float.Lerp(At(x0, y0), At(x0 + 1, y0), fx);
        var north = float.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx);
        return float.Lerp(south, north, fy);
    }
}
