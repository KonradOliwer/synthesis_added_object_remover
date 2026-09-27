using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

internal readonly record struct ExteriorCell(FormKey WorldspaceKey, int X, int Y);

/// <summary>
/// Terrain height of exterior cells from the winning LAND record's vertex height map (VHGT): 33x33
/// vertices 128 units apart covering one 4096-unit cell, the last row and column shared with the
/// neighbouring cells. Heights are stored as deltas: the first byte of each row is relative to the
/// first vertex of the previous row (the first row to the record's offset), every other byte to the
/// vertex before it in its row, and the running value times 8 is the height in game units. Between
/// vertices the height is interpolated bilinearly. Each cell is decoded once, on first use.
/// Thread-safe.
/// </summary>
/// <param name="landscapes">LAND records by the cell of the worldspace that holds them.</param>
/// <param name="landWorldspaces">Target worldspace -> the worldspace whose LAND records it uses (itself, or a parent whose land data it uses).</param>
internal sealed class TerrainHeights(
    IReadOnlyDictionary<ExteriorCell, ILandscapeGetter> landscapes,
    IReadOnlyDictionary<FormKey, FormKey> landWorldspaces)
{
    private const int VerticesPerSide = 33;
    private const float VertexSpacing = ExteriorGrid.CellSize / (VerticesPerSide - 1);
    private const float UnitsPerHeightStep = 8f;

    private readonly LazyCache<ExteriorCell, float[]?> _heightsByCell = new();
    private readonly HashSet<FormKey> _worldspacesWithTerrain = landscapes.Keys.Select(cell => cell.WorldspaceKey).ToHashSet();

    /// <summary>False for interior cells and worldspaces whose land has no LAND record.</summary>
    public bool HasTerrain(FormKey spaceKey) =>
        landWorldspaces.TryGetValue(spaceKey, out var landWorldspace) && _worldspacesWithTerrain.Contains(landWorldspace);

    /// <param name="worldspaceKey">A worldspace with terrain (<see cref="HasTerrain"/>).</param>
    /// <returns>False where the worldspace has no terrain (no, a deleted, or an empty LAND record).</returns>
    public bool TryGetHeight(FormKey worldspaceKey, Vector2 position, out float height)
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

    /// <param name="worldspaceKey">A worldspace with terrain (<see cref="HasTerrain"/>).</param>
    /// <returns>False where the cell has no terrain (no, a deleted, or an empty LAND record).</returns>
    public bool HasHeights(FormKey worldspaceKey, int cellX, int cellY) => GetHeights(worldspaceKey, cellX, cellY) != null;

    private float[]? GetHeights(FormKey worldspaceKey, int cellX, int cellY)
    {
        var cell = new ExteriorCell(landWorldspaces[worldspaceKey], cellX, cellY);
        return _heightsByCell.GetOrCreate(cell, () => DecodeHeights(cell));
    }

    private float[]? DecodeHeights(ExteriorCell cell)
    {
        if (!landscapes.TryGetValue(cell, out var landscape) || landscape.IsDeleted) return null;
        if (landscape.VertexHeightMap is not { } map) return null;

        var heights = new float[VerticesPerSide * VerticesPerSide];
        var rowStart = map.Offset;
        for (var y = 0; y < VerticesPerSide; y++)
        {
            rowStart += map.HeightMap[0, y];
            var value = rowStart;
            heights[y * VerticesPerSide] = value * UnitsPerHeightStep;
            for (var x = 1; x < VerticesPerSide; x++)
            {
                value += map.HeightMap[x, y];
                heights[y * VerticesPerSide + x] = value * UnitsPerHeightStep;
            }
        }
        return heights;
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
