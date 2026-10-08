using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

public class TerrainHeightsGridTests
{
    private static readonly RecordKey Worldspace = TestTargets.SpaceKey(0x500);
    private static readonly RecordKey Child = TestTargets.SpaceKey(0x501);

    private static float[] SlopeInX()
    {
        var heights = new float[TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide];
        for (var i = 0; i < heights.Length; i++) heights[i] = i % TerrainHeights.VerticesPerSide * 10f;
        return heights;
    }

    private static TerrainHeights CreateTerrain(Dictionary<ExteriorCell, int> reads) => new(
        cell =>
        {
            reads[cell] = reads.GetValueOrDefault(cell) + 1;
            return cell is { X: 0, Y: 0 } ? SlopeInX() : null;
        },
        space => space == Worldspace || space == Child ? Worldspace : null);

    [Fact]
    public void InterpolatesBetweenVerticesOfTheCellsGrid()
    {
        var terrain = CreateTerrain([]);
        Assert.True(terrain.TryGetHeight(Child, new Vector2(64, 500), out var height));
        Assert.Equal(5f, height, 1e-3f);
    }

    [Fact]
    public void CellWithoutAGridHasNoHeights()
    {
        var terrain = CreateTerrain([]);
        Assert.False(terrain.HasHeights(Worldspace, 1, 0));
        Assert.True(terrain.HasHeights(Worldspace, 0, 0));
    }

    [Fact]
    public void EachCellsGridIsReadOnce()
    {
        var reads = new Dictionary<ExteriorCell, int>();
        var terrain = CreateTerrain(reads);
        terrain.TryGetHeight(Worldspace, new Vector2(1, 1), out _);
        terrain.TryGetHeight(Worldspace, new Vector2(2, 2), out _);
        terrain.HasHeights(Worldspace, 0, 0);
        Assert.Equal(1, reads[new ExteriorCell(Worldspace, 0, 0)]);
    }
}
