using System.Numerics;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

/// <summary>The corner heights of a terrain square, and the places under a box.</summary>
public class TerrainGridSurfaceTests
{
    private const int Side = TerrainGrid.VerticesPerSide;

    [Fact]
    public void TheCornersOfASquareAreReadFromItsRows()
    {
        var heights = new float[TerrainGrid.VertexCount];
        (heights[0], heights[1], heights[Side], heights[Side + 1]) = (10f, 20f, 30f, 60f);

        Assert.Equal((10f, 20f, 30f, 60f), TerrainGrid.CornerHeights(heights, 0));
    }

    /// <summary>A box narrower than a vertex spacing gives its corners; a wider one also the places one spacing apart between them.</summary>
    [Fact]
    public void ThePlacesUnderABoxAreItsCornersAndPlacesOneVertexSpacingApart()
    {
        var narrow = new Box(new Vector3(10, 20, 0), new Vector3(50, 60, 5));
        var wide = new Box(new Vector3(0, 0, 0), new Vector3(200, 10, 5));

        Assert.Equal(new Vector2[] { new(10, 20), new(10, 60), new(50, 20), new(50, 60) }, TerrainGrid.PlacesUnder(narrow));
        Assert.Equal(new Vector2[] { new(0, 0), new(0, 10), new(128, 0), new(128, 10), new(200, 0), new(200, 10) }, TerrainGrid.PlacesUnder(wide));
    }
}
