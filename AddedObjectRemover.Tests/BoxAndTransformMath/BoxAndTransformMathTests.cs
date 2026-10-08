using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.BoxAndTransformMath;

public class BoxAndTransformMathTests
{
    [Fact]
    public void InsetMovesEverySideInward()
    {
        var inset = new Box(new Vector3(-20, -30, 0), new Vector3(20, 30, 100)).Inset(5);

        Assert.Equal(new Box(new Vector3(-15, -25, 5), new Vector3(15, 25, 95)), inset);
    }

    [Fact]
    public void InsetOfAThinBoxKeepsItsCentreLineOnEveryAxis()
    {
        var inset = new Box(new Vector3(-2, -3, 10), new Vector3(4, 5, 16)).Inset(5);

        Assert.Equal(new Box(new Vector3(1, 1, 13), new Vector3(1, 1, 13)), inset);
    }

    [Fact]
    public void UnionAllEnclosesEveryBox()
    {
        var union = Box.UnionAll([
            new Box(new Vector3(0), new Vector3(1)),
            new Box(new Vector3(-2, 0, 0), new Vector3(0, 5, 1)),
            new Box(new Vector3(0, 0, -3), new Vector3(1, 1, 0)),
        ]);

        Assert.Equal(new Box(new Vector3(-2, 0, -3), new Vector3(1, 5, 1)), union);
    }

    [Fact]
    public void UnionAllOfNoBoxesThrows() =>
        Assert.Throws<InvalidOperationException>(() => Box.UnionAll([]));

    [Fact]
    public void UprightTransformTurnsOnlyAboutZ()
    {
        var position = new Vector3(1, 2, 3);

        var upright = PlacedTransform.Upright(position, 0.7f, 2f);

        Assert.Equal(position, upright.Position);
        Assert.Equal(2f, upright.Scale);
        Assert.Equal(Mat3.FromEuler(new Vector3(0, 0, 0.7f)), upright.Rotation);
        Assert.Equal(Vector3.UnitZ, upright.Rotation.Transform(Vector3.UnitZ));
    }

    [Theory]
    [InlineData(10f, 4f, 5f)]
    [InlineData(4f, 10f, 5f)]
    [InlineData(-2f, -6f, -1f)]
    public void HorizontalHalfSizeIsHalfOfTheLargerHorizontalSize(float sizeX, float sizeY, float expected) =>
        Assert.Equal(expected, Boxes.HorizontalHalfSize(new Vector3(sizeX, sizeY, 1000f)));

    [Theory]
    [InlineData(0f, 0f, 0f, true)]
    [InlineData(100f, -100f, 100f, true)]
    [InlineData(100.5f, 0f, 0f, false)]
    [InlineData(0f, -100.5f, 0f, false)]
    [InlineData(0f, 0f, 100.5f, false)]
    [InlineData(float.NaN, 0f, 0f, false)]
    [InlineData(float.PositiveInfinity, 0f, 0f, false)]
    public void WithinLimitUsesTheGivenLimitOnEveryComponent(float x, float y, float z, bool expected) =>
        Assert.Equal(expected, Vectors.IsWithinLimit(new Vector3(x, y, z), 100f));

    [Fact]
    public void ExpandedLocalBoxGrowsBySizeTimesMultiplier()
    {
        var grown = Boxes.ExpandedLocalBox(new Box(Vector3.Zero, new Vector3(2, 4, 6)), scale: 2, multiplier: 0.5f);

        Assert.Equal(new Box(new Vector3(-2, -4, -6), new Vector3(6, 12, 18)), grown);
    }

    [Theory]
    [InlineData(15f, 6f, 7f, true)]
    [InlineData(11f, 2f, 3f, true)]
    [InlineData(15.001f, 6f, 7f, false)]
    [InlineData(11f, 1.999f, 3f, false)]
    [InlineData(13f, 4f, 7.001f, false)]
    public void IsInsideOrientedBoxCountsPointsExactlyOnTheLocalMinAndMaxFacesAsInside(float x, float y, float z, bool expected)
    {
        var localBox = new Box(new Vector3(1, 2, 3), new Vector3(5, 6, 7));

        Assert.Equal(expected, Boxes.IsInsideOrientedBox(new Vector3(x, y, z), new Vector3(10, 0, 0), Mat3.Identity, localBox));
    }

    [Fact]
    public void EnlargeAroundCentreKeepsTheCentreInPlace()
    {
        var transform = new PlacedTransform(new Vector3(10, 20, 30), Mat3.FromEuler(new Vector3(0, 0, 0.6f)), 2f);
        var centre = new Vector3(1, 2, 3);

        var enlarged = Boxes.EnlargeAroundCentre(transform, centre, 3f);

        Assert.Equal(6f, enlarged.Scale);
        VectorAssert.Near(
            transform.Position + transform.Rotation.Transform(centre * transform.Scale),
            enlarged.Position + enlarged.Rotation.Transform(centre * enlarged.Scale));
    }

    [Fact]
    public void SortedScaledDimensionsGoLargestFirst()
    {
        var sorted = Boxes.SortedScaledDimensions(new Box(Vector3.Zero, new Vector3(2, 6, 4)), 0.5f);

        Assert.Equal(new SortedDimensions(3f, 2f, 1f), sorted);
    }

    [Fact]
    public void OriginFractionIsWhereZeroSitsBetweenMinAndMax()
    {
        var fraction = Boxes.OriginFraction(new Box(new Vector3(-1, 0, -3), new Vector3(3, 0, 1)));

        Assert.Equal(0.25f, fraction.X);
        Assert.True(float.IsNaN(fraction.Y));
        Assert.Equal(0.75f, fraction.Z);
    }

    [Theory]
    [InlineData(5f, 5f, 0f)]
    [InlineData(-3f, 5f, 3f)]
    [InlineData(13f, 14f, 5f)]
    public void HorizontalDistanceIgnoresHeightAndIsZeroOverTheFootprint(float x, float y, float expected) =>
        Assert.Equal(expected, Boxes.HorizontalDistance(new Box(Vector3.Zero, new Vector3(10)), new Vector3(x, y, 500)));

    [Fact]
    public void WorldAabbPlacesTheLocalBox()
    {
        var box = Boxes.WorldAabb(new Box(new Vector3(-1, -2, 0), new Vector3(1, 2, 4)), new Vector3(10, 0, 0), Mat3.FromEuler(new Vector3(0, 0, MathF.PI / 2)));

        VectorAssert.Near(new Vector3(8, -1, 0), box.Min);
        VectorAssert.Near(new Vector3(12, 1, 4), box.Max);
    }

    [Theory]
    [InlineData(100f, 100f, 110f, 10f, nameof(DirectionSector.North))]
    [InlineData(100f, 100f, 10f, 110f, nameof(DirectionSector.East))]
    [InlineData(-100f, -100f, 10f, 110f, nameof(DirectionSector.West))]
    public void TowardsAnOffAxisBoxPointsAtItsClosestPointNotItsCentre(
        float centreX, float centreY, float halfX, float halfY, string expected)
    {
        var box = new OrientedBox(new Vector3(centreX, centreY, 0), Mat3.Identity, new Vector3(halfX, halfY, 1));

        Assert.Equal(Enum.Parse<DirectionSector>(expected), CompassDirections.Towards(Vector3.Zero, box));
    }

    [Theory]
    [InlineData(30f, nameof(DirectionSector.North))]
    [InlineData(-30f, nameof(DirectionSector.North))]
    public void TowardsABoxStraightAboveOrBelowPointsAtItsCentre(float centreZ, string expected)
    {
        var box = new OrientedBox(new Vector3(0, 50, centreZ), Mat3.Identity, new Vector3(5, 100, 5));

        Assert.Equal(Enum.Parse<DirectionSector>(expected), CompassDirections.Towards(Vector3.Zero, box));
    }

    [Fact]
    public void CellAreaCoversTheCellsOfABoxAndItsRing()
    {
        var area = CellArea.Covering(new Box(new Vector3(-1, 0, 0), new Vector3(4097, 10, 0)));

        Assert.Equal(new CellArea(-1, 0, 1, 0), area);
        Assert.Equal(new CellArea(-2, -1, 2, 1), area.WithNeighbors());
        Assert.Equal(3, area.Cells().Count());
        Assert.True(area.Contains(new Vector3(-1, 5, 0)));
        Assert.False(area.Contains(new Vector3(0, 4096, 0)));
        Assert.True(area.ContainsInset(new Vector3(0, 100, 0), 50f));
        Assert.False(area.ContainsInset(new Vector3(-4090, 100, 0), 50f));
    }

    [Theory]
    [InlineData(1f, 2f, 3f, true)]
    [InlineData(float.NaN, 2f, 3f, false)]
    [InlineData(1f, float.NegativeInfinity, 3f, false)]
    [InlineData(1f, 2f, float.PositiveInfinity, false)]
    public void FiniteNeedsEveryComponentFinite(float x, float y, float z, bool expected) =>
        Assert.Equal(expected, Vectors.IsFinite(new Vector3(x, y, z)));
}
