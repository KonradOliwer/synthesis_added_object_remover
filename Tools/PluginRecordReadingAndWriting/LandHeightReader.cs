using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Decodes the vertex height map (VHGT) of a LAND record: 33x33 vertices covering one cell, the
/// last row and column shared with the neighbouring cells. Heights are stored as deltas: the first
/// byte of each row is relative to the first vertex of the previous row (the first row to the
/// record's offset), every other byte to the vertex before it in its row, and the running value
/// times 8 is the height in game units.
/// </summary>
internal static class LandHeightReader
{
    private const int VerticesPerSide = TerrainHeights.VerticesPerSide;
    private const float UnitsPerHeightStep = 8f;

    /// <returns>Heights row by row (<see cref="VerticesPerSide"/> per row); null for a deleted record or one without a height map.</returns>
    public static float[]? Read(ILandscapeGetter landscape)
    {
        if (landscape.IsDeleted) return null;
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
}
