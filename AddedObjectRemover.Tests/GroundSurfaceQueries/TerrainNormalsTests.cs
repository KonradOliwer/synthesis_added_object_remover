using System.Numerics;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

/// <summary>Terrain normals point up on flat ground and lean away from the rising side of a slope.</summary>
public class TerrainNormalsTests
{
    private const int Side = TerrainHeights.VerticesPerSide;
    private const float Spacing = TerrainGrid.VertexSpacing;
    private const float Precision = 1e-5f;

    [Fact]
    public void FlatGroundPointsStraightUp()
    {
        var normals = TerrainNormals.Compute(Heights((_, _) => 100f), CellEdgeHeights.Unknown);

        Assert.All(normals, normal => AssertClose(Vector3.UnitZ, normal));
    }

    [Fact]
    public void GroundRisingEastwardLeansTheNormalWest()
    {
        const float rise = 32f;
        var normals = TerrainNormals.Compute(Heights((x, _) => x * rise), CellEdgeHeights.Unknown);

        AssertClose(Vector3.Normalize(new Vector3(-rise / Spacing, 0, 1)), normals[10 * Side + 10]);
    }

    [Fact]
    public void GroundRisingNorthwardLeansTheNormalSouth()
    {
        const float rise = 16f;
        var normals = TerrainNormals.Compute(Heights((_, y) => y * rise), CellEdgeHeights.Unknown);

        AssertClose(Vector3.Normalize(new Vector3(0, -rise / Spacing, 1)), normals[20 * Side + 3]);
    }

    [Fact]
    public void AnEdgePointUsesTheNeighbouringCellsHeightWhenKnownAndItsOwnSideOtherwise()
    {
        var heights = Heights((x, _) => x == Side - 1 ? 64f : 0f);
        var east = Enumerable.Repeat(192f, Side).ToArray();

        var withNeighbour = TerrainNormals.Compute(heights, new CellEdgeHeights(null, east, null, null));
        var withoutNeighbour = TerrainNormals.Compute(heights, CellEdgeHeights.Unknown);

        var edge = 5 * Side + Side - 1;
        AssertClose(Vector3.Normalize(new Vector3(-(192f - 0f) / (2 * Spacing), 0, 1)), withNeighbour[edge]);
        AssertClose(Vector3.Normalize(new Vector3(-(64f - 0f) / Spacing, 0, 1)), withoutNeighbour[edge]);
    }

    private static float[] Heights(Func<int, int, float> heightAt) =>
        [.. Enumerable.Range(0, Side * Side).Select(point => heightAt(point % Side, point / Side))];

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Precision, $"Expected {expected}, got {actual}.");
}
