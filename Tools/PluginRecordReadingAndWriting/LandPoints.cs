using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// The values at a LAND record's grid points, row by row from the bottom, x fastest: what its point parts compare and copy. Each
/// grid is null when the record has none.
/// </summary>
/// <param name="heights">In game units.</param>
internal sealed class LandPoints(float[]? heights, P3UInt8[]? normals, P3UInt8[]? colours)
{
    private const int VerticesPerSide = TerrainHeights.VerticesPerSide;

    public float[]? Heights => heights;

    public P3UInt8[]? Normals => normals;

    public P3UInt8[]? Colours => colours;

    public static LandPoints Read(ILandscapeGetter landscape) =>
        new(LandHeightReader.Read(landscape), ReadGrid(landscape.VertexNormals), ReadGrid(landscape.VertexColors));

    private static P3UInt8[]? ReadGrid(IReadOnlyArray2d<P3UInt8>? grid)
    {
        if (grid is null) return null;
        var values = new P3UInt8[VerticesPerSide * VerticesPerSide];
        for (var y = 0; y < VerticesPerSide; y++)
        {
            for (var x = 0; x < VerticesPerSide; x++) values[y * VerticesPerSide + x] = grid[x, y];
        }
        return values;
    }
}
