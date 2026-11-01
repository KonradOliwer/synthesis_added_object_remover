using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Heights stored in a LAND record's height map and read back as the game reads them.</summary>
internal static class TestLandHeights
{
    private const int Side = TerrainHeights.VerticesPerSide;

    /// <summary>Writes the encoded heights into a LAND record's height map and reads them back.</summary>
    public static float[] Decode(float[] heights)
    {
        var steps = LandHeightEncoder.Encode(heights)!;
        var map = new LandscapeVertexHeightMap { Offset = steps.Offset, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) };
        for (var point = 0; point < steps.Steps.Length; point++) map.HeightMap[point % Side, point / Side] = steps.Steps[point];
        var land = new Landscape(FormKey.Factory("000800:Base.esm"), SkyrimRelease.SkyrimSE) { VertexHeightMap = map };
        return LandHeightReader.Read(land)!;
    }
}
