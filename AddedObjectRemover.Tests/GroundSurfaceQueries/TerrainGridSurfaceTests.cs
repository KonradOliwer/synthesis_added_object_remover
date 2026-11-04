using System.Numerics;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

/// <summary>The terrain surface of one square drawn as two triangles, the points that bound it over part of a square, and the parts of a rectangle in each square.</summary>
public class TerrainGridSurfaceTests
{
    private const int Side = TerrainGrid.VerticesPerSide;
    private const float Step = TerrainGrid.VertexSpacing;

    /// <summary>The first square of a cell with its corners at 10 (south-west), 20 (south-east), 30 (north-west) and 60 (north-east).</summary>
    private static readonly float[] Heights = HeightsWithCorners(10f, 20f, 30f, 60f);

    [Theory]
    [InlineData(SquareSplit.SouthWestToNorthEast)]
    [InlineData(SquareSplit.SouthEastToNorthWest)]
    public void TheSurfaceMeetsEachCornerOnEitherSplit(SquareSplit split)
    {
        Assert.Equal(
            [10f, 20f, 30f, 60f],
            new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One }.Select(corner => TerrainGrid.HeightOnTriangles(Heights, 0, corner, split)));
    }

    /// <summary>
    /// At the centre the south-west to north-east split runs along its diagonal, halfway between 10 and 60; the other split runs along
    /// its own diagonal, halfway between 20 and 30.
    /// </summary>
    [Theory]
    [InlineData(SquareSplit.SouthWestToNorthEast, 35f)]
    [InlineData(SquareSplit.SouthEastToNorthWest, 25f)]
    public void AtTheCentreEachSplitLiesOnItsOwnDiagonal(SquareSplit split, float expected)
    {
        Assert.Equal(expected, TerrainGrid.HeightOnTriangles(Heights, 0, new Vector2(0.5f), split), 4);
    }

    /// <summary>On the triangle south-east of the south-west to north-east diagonal the surface is the plane through 10, 20 and 60.</summary>
    [Fact]
    public void InsideATriangleTheSurfaceIsThePlaneThroughItsCorners()
    {
        Assert.Equal(10f + 0.75f * 10f + 0.25f * 40f, TerrainGrid.HeightOnTriangles(Heights, 0, new Vector2(0.75f, 0.25f), SquareSplit.SouthWestToNorthEast), 4);
    }

    [Fact]
    public void TheCornersOfASquareAreReadFromItsRows()
    {
        Assert.Equal((10f, 20f, 30f, 60f), TerrainGrid.CornerHeights(Heights, 0));
    }

    /// <summary>A part from (0.2, 0.4) to (0.6, 0.8) is crossed by the south-west to north-east diagonal on its south and west edges.</summary>
    [Fact]
    public void APartCrossingTheDiagonalIsBoundedByItsCornersAndItsCrossings()
    {
        var part = new SquarePart(0, 0, 0, new Vector2(0.2f, 0.4f), new Vector2(0.6f, 0.8f));

        var points = TerrainGrid.BoundingPointsOnTriangles(part, SquareSplit.SouthWestToNorthEast).ToList();

        Assert.Equal(
            [new Vector2(0.2f, 0.4f), new Vector2(0.6f, 0.4f), new Vector2(0.2f, 0.8f), new Vector2(0.6f, 0.8f), new Vector2(0.6f, 0.6f), new Vector2(0.4f, 0.4f)],
            points);
    }

    /// <summary>A part from (0.2, 0.4) to (0.5, 0.7) is crossed by the south-east to north-west diagonal on its east and north edges.</summary>
    [Fact]
    public void APartCrossingTheOtherDiagonalIsBoundedByThoseCrossings()
    {
        var part = new SquarePart(0, 0, 0, new Vector2(0.2f, 0.4f), new Vector2(0.5f, 0.7f));

        var crossings = TerrainGrid.BoundingPointsOnTriangles(part, SquareSplit.SouthEastToNorthWest).Skip(4).ToList();

        Assert.Equal(2, crossings.Count);
        Assert.All(crossings, point => Assert.Equal(1f, point.X + point.Y, 4));
        Assert.Contains(crossings, point => MathF.Abs(point.X - 0.5f) < 1e-6f);
        Assert.Contains(crossings, point => MathF.Abs(point.Y - 0.7f) < 1e-6f);
    }

    /// <summary>A part wholly on one side of the diagonal is bounded by its corners alone.</summary>
    [Fact]
    public void APartAwayFromTheDiagonalIsBoundedByItsCornersAlone()
    {
        var part = new SquarePart(0, 0, 0, new Vector2(0.6f, 0.1f), new Vector2(0.9f, 0.3f));

        Assert.Equal(4, TerrainGrid.BoundingPointsOnTriangles(part, SquareSplit.SouthWestToNorthEast).Count());
    }

    /// <summary>
    /// A rectangle from half a step west of cell (1, 0)'s west edge to a quarter step east of it, within one row of squares: the last
    /// square of cell (0, 0) holds its west half, the first square of cell (1, 0) its east quarter.
    /// </summary>
    [Fact]
    public void ARectangleAcrossACellEdgeIsSplitIntoTheSquaresOfEachCell()
    {
        var edge = ExteriorGrid.CellSize;
        var rectangle = (new Vector2(edge - 0.5f * Step, 2.25f * Step), new Vector2(edge + 0.25f * Step, 2.75f * Step));

        var parts = TerrainGrid.SquaresUnder(rectangle).ToList();

        Assert.Equal(
            [
                new SquarePart(0, 0, 2 * Side + TerrainGrid.SquaresPerSide - 1, new Vector2(0.5f, 0.25f), new Vector2(1f, 0.75f)),
                new SquarePart(1, 0, 2 * Side, new Vector2(0f, 0.25f), new Vector2(0.25f, 0.75f)),
            ],
            parts);
    }

    private static float[] HeightsWithCorners(float southWest, float southEast, float northWest, float northEast)
    {
        var heights = new float[TerrainGrid.VertexCount];
        (heights[0], heights[1], heights[Side], heights[Side + 1]) = (southWest, southEast, northWest, northEast);
        return heights;
    }
}
