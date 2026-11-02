using Mutagen.Bethesda.Skyrim;
using Noggog;
using static AddedObjectRemover.TerrainGrid;

namespace AddedObjectRemover;

/// <summary>
/// Decodes the vertex height map (VHGT) of a LAND record: 33x33 vertices covering one cell, the
/// last row and column shared with the neighbouring cells. Heights are stored as deltas: the first
/// byte of each row is relative to the first vertex of the previous row (the first row to the
/// record's offset), every other byte to the vertex before it in its row, and the running value
/// times <see cref="TerrainGrid.UnitsPerHeightStep"/> is the height in game units.
/// </summary>
internal static class LandHeightReader
{
    /// <returns>Heights row by row (<see cref="TerrainGrid.VerticesPerSide"/> per row); null for a deleted record or one without a height map.</returns>
    public static float[]? Read(ILandscapeGetter landscape)
    {
        if (landscape.IsDeleted) return null;
        if (MutagenCalls.Read(() => landscape.VertexHeightMap) is not { } map) return null;
        var (offset, steps) = MutagenCalls.Read(() => (map.Offset, map.HeightMap.ToArray()));
        return Decode(offset, StepsByVertex(steps));
    }

    /// <returns>The height steps row by row, read out of the map by the positions it gives.</returns>
    private static sbyte[] StepsByVertex(IEnumerable<IKeyValue<P2Int, sbyte>> steps)
    {
        var byVertex = new sbyte[VertexCount];
        foreach (var step in steps) byVertex[step.Key.Y * VerticesPerSide + step.Key.X] = step.Value;
        return byVertex;
    }

    private static float[] Decode(float offset, sbyte[] steps)
    {
        var heights = new float[VertexCount];
        var rowStart = offset;
        for (var y = 0; y < VerticesPerSide; y++)
        {
            rowStart += steps[y * VerticesPerSide];
            var value = rowStart;
            heights[y * VerticesPerSide] = value * UnitsPerHeightStep;
            for (var x = 1; x < VerticesPerSide; x++)
            {
                value += steps[y * VerticesPerSide + x];
                heights[y * VerticesPerSide + x] = value * UnitsPerHeightStep;
            }
        }
        return heights;
    }
}
