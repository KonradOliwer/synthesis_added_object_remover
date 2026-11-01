using System.Numerics;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

/// <summary>
/// Land grid points across cells: which records hold a world grid point, the grid steps around a point, the quadrants holding a
/// cell's vertex and their centres, and the heights beside a cell's edges.
/// </summary>
public class LandGridPointsTests
{
    private const int Side = TerrainGrid.VerticesPerSide;
    private const int Last = Side - 1;

    private static readonly RecordKey Worldspace = new(new PluginName("Base.esm"), 0x100);

    [Fact]
    public void APointInsideACellIsHeldByThatCellAlone()
    {
        var point = new LandGridPoint(new ExteriorCell(Worldspace, 2, -1), 5 * Side + 7);

        Assert.Equal([point], WorldGridPoint.Of(point).Holders());
    }

    [Fact]
    public void APointOnACellEdgeIsHeldByBothCellsWithTheCellItStartsInFirst()
    {
        var onEastEdge = new LandGridPoint(new ExteriorCell(Worldspace, 0, 0), 5 * Side + Last);

        Assert.Equal(
            [new LandGridPoint(new ExteriorCell(Worldspace, 1, 0), 5 * Side), onEastEdge],
            WorldGridPoint.Of(onEastEdge).Holders());
    }

    [Fact]
    public void APointOnACellCornerIsHeldByFourCells()
    {
        var corner = WorldGridPoint.Of(new LandGridPoint(new ExteriorCell(Worldspace, -1, -1), Last * Side + Last));

        Assert.Equal(
            [
                new LandGridPoint(new ExteriorCell(Worldspace, 0, 0), 0),
                new LandGridPoint(new ExteriorCell(Worldspace, 0, -1), Last * Side),
                new LandGridPoint(new ExteriorCell(Worldspace, -1, 0), Last),
                new LandGridPoint(new ExteriorCell(Worldspace, -1, -1), Last * Side + Last),
            ],
            corner.Holders());
    }

    [Fact]
    public void TheStepsWithinADistanceComeNearestFirstAndStopAtTheDistance()
    {
        var steps = GridStep.Within(1);

        Assert.Equal([(0, -1), (-1, 0), (1, 0), (0, 1)], steps.Select(step => (step.X, step.Y)));
        Assert.Equal(12, GridStep.Within(2).Length);
        Assert.Empty(GridStep.Within(0));
    }

    [Theory]
    [InlineData(5, 5, new[] { 0 })]
    [InlineData(16, 5, new[] { 0, 1 })]
    [InlineData(20, 16, new[] { 1, 3 })]
    [InlineData(16, 16, new[] { 0, 1, 2, 3 })]
    [InlineData(32, 32, new[] { 3 })]
    public void AVertexOnALineBetweenQuadrantsLiesInEachOfThem(int x, int y, int[] quadrants)
    {
        Assert.Equal(quadrants, TerrainQuadrants.Holding(y * Side + x));
    }

    [Fact]
    public void AQuadrantsCentreLiesAQuarterAndThreeQuartersAcrossItsCell()
    {
        Assert.Equal(new Vector2(-4096 + 3072, 4096 + 1024), TerrainQuadrants.CentreOf(-1, 1, TerrainQuadrants.IndexOf(1, 0)));
    }

    [Fact]
    public void TheHeightsBesideACellAreTheSecondLinesOfItsNeighboursGrids()
    {
        float[] Grid(float scale) => [.. Enumerable.Range(0, Side * Side).Select(point => scale * point)];

        var beside = CellEdgeHeights.Beside(Grid(1), Grid(2), null, Grid(3));

        Assert.Equal((Last - 1f, 2f * 1, 3f * Side), (beside.West![0], beside.East![0], beside.North![0]));
        Assert.Null(beside.South);
    }

    [Fact]
    public void TheVerticesUnderABoxAreThoseUnderItsRectangleSeenFromAbove()
    {
        var box = new Box(new Vector3(100, 100, -500), new Vector3(300, 140, 500));

        var under = Assert.Single(TerrainGrid.VerticesUnder(box));

        Assert.Equal(TerrainGrid.VerticesUnder((new Vector2(100, 100), new Vector2(300, 140))).Single().VertexIndices, under.VertexIndices);
    }
}
