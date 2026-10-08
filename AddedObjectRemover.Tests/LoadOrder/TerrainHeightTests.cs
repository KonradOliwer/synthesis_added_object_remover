using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class TerrainHeightTests
{
    private const int VerticesPerSide = 33;
    private const float VertexSpacing = 128f;
    private const float Offset = 10f;

    private static readonly RecordKey Worldspace = TestTargets.SpaceKey(0x200);
    private static readonly RecordKey ChildWorldspace = TestTargets.SpaceKey(0x201);
    private static readonly RecordKey Interior = TestTargets.SpaceKey(0x202);

    /// <summary>
    /// Row starts rise by 2 steps per row (the first by 1 over the offset), every vertex by 1 step
    /// over the one before it, so the height is (offset + 1 + 2y + x) * 8 at vertex (x, y).
    /// </summary>
    private static Landscape SlopedLandscape(uint id)
    {
        var map = new LandscapeVertexHeightMap { Offset = Offset };
        for (var y = 0; y < VerticesPerSide; y++)
        {
            map.HeightMap[0, y] = (sbyte)(y == 0 ? 1 : 2);
            for (var x = 1; x < VerticesPerSide; x++) map.HeightMap[x, y] = 1;
        }
        return new Landscape(new FormKey(TestTargets.TargetMod, id), SkyrimRelease.SkyrimSE) { VertexHeightMap = map };
    }

    private static float Expected(float vertexX, float vertexY) => (Offset + 1 + 2 * vertexY + vertexX) * 8;

    private static TerrainHeights CreateTerrain() => TestGround.Terrain(
        new Dictionary<ExteriorCell, ILandscapeGetter>
        {
            [new ExteriorCell(Worldspace, 0, 0)] = SlopedLandscape(0x300),
            [new ExteriorCell(Worldspace, 1, -1)] = SlopedLandscape(0x301),
        },
        new Dictionary<RecordKey, RecordKey> { [Worldspace] = Worldspace, [ChildWorldspace] = Worldspace });

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(31.99f, 31.99f)]
    [InlineData(5.5f, 7.25f)]
    [InlineData(31.9f, 0.1f)]
    public void DecodesDeltasAndInterpolatesBetweenVertices(float vertexX, float vertexY)
    {
        var terrain = CreateTerrain();
        Assert.True(terrain.TryGetHeight(Worldspace, new Vector2(vertexX, vertexY) * VertexSpacing, out var height));
        Assert.Equal(Expected(vertexX, vertexY), height, 1e-2f);
    }

    [Fact]
    public void NegativeCellUsesItsOwnSouthWestCorner()
    {
        var terrain = CreateTerrain();
        var position = new Vector2(ExteriorGrid.CellSize + 0.5f * VertexSpacing, -ExteriorGrid.CellSize + 3 * VertexSpacing);
        Assert.True(terrain.TryGetHeight(ChildWorldspace, position, out var height));
        Assert.Equal(Expected(0.5f, 3), height, 1e-2f);
    }

    [Fact]
    public void CellWithoutLandHasNoHeight()
    {
        var terrain = CreateTerrain();
        Assert.False(terrain.TryGetHeight(Worldspace, new Vector2(-10, 10), out _));
        Assert.False(terrain.TryGetHeight(Worldspace, new Vector2(ExteriorGrid.CellSize, 10), out _));
        Assert.True(terrain.HasTerrain(ChildWorldspace));
        Assert.False(terrain.HasTerrain(Interior));
    }
}
