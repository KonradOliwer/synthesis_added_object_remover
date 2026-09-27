using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The directions a visible target object counts in around an invisible object.</summary>
public class LeftoverDirectionTests
{
    private static readonly Box Table = new(new Vector3(-50, -30, 0), new Vector3(50, 30, 40));

    private static OrientedBox Place(Box local, Vector3 position, float zRadians = 0f) =>
        OrientedBox.FromLocal(local, TestTargets.At(position, zRadians));

    [Theory]
    [InlineData(0f, 0f, 20f)]
    [InlineData(49.9f, 29.9f, 0.1f)]
    [InlineData(49.99f, -29.99f, 39.99f)]
    [InlineData(-12.3f, 7.7f, 39.99f)]
    public void PointInsideARotatedBoxCountsInEveryDirection(float x, float y, float z)
    {
        var position = new Vector3(1234.5f, -987.25f, 10f);
        var box = Place(Table, position, zRadians: 0.7f);
        var point = position + TestTargets.At(Vector3.Zero, 0.7f).ToWorld(new Vector3(x, y, z));

        Assert.Equal(SectorAreas.All, VisibleTargetIndex.FindSectors(box, point));
    }

    [Theory]
    [InlineData(0.01f, 0f, nameof(DirectionSector.West))]
    [InlineData(-0.01f, 0f, nameof(DirectionSector.East))]
    [InlineData(0f, -0.01f, nameof(DirectionSector.North))]
    [InlineData(-30f, 20f, nameof(DirectionSector.SouthEast))]
    public void ObjectStraightBelowCountsOnlyTowardsItsCentre(float pointX, float pointY, string expected)
    {
        var box = Place(Table, Vector3.Zero);
        var sector = Enum.Parse<DirectionSector>(expected);

        AssertOnly(sector, VisibleTargetIndex.FindSectors(box, new Vector3(pointX, pointY, 41f)));
        AssertOnly(sector, VisibleTargetIndex.FindSectors(box, new Vector3(pointX, pointY, -500f)));
    }

    [Fact]
    public void CentreExactlyBelowCountsInTheFixedDirection()
    {
        var box = Place(Table, new Vector3(300, 400, 0));

        AssertOnly(SectorAreas.NoHorizontalOffset, VisibleTargetIndex.FindSectors(box, new Vector3(300, 400, 41f)));
    }

    [Fact]
    public void RotatedFootprintDecidesWhatLiesStraightBelow()
    {
        var beam = Place(new Box(new Vector3(-100, -5, 0), new Vector3(100, 5, 10)), Vector3.Zero, zRadians: MathF.PI / 4);
        // Rz(-45°) turns the beam's +X end to the south-east; its axis runs from (-70.7, 70.7) to (70.7, -70.7).
        var overTheBeam = new Vector3(50, -50, 20);
        var besideTheBeam = new Vector3(50, 50, 20);

        Assert.True(beam.IsCrossedByVerticalLine(overTheBeam));
        AssertOnly(DirectionSector.NorthWest, VisibleTargetIndex.FindSectors(beam, overTheBeam));
        Assert.False(beam.IsCrossedByVerticalLine(besideTheBeam));
        AssertOnly(DirectionSector.SouthWest, VisibleTargetIndex.FindSectors(beam, besideTheBeam));
    }

    [Fact]
    public void ObjectBesideCountsTowardsItsClosestPoint()
    {
        var box = Place(Table, new Vector3(100, 60, 0));

        AssertOnly(DirectionSector.East, VisibleTargetIndex.FindSectors(box, new Vector3(0, 40, 20)));
        AssertOnly(DirectionSector.NorthEast, VisibleTargetIndex.FindSectors(box, new Vector3(0, 0, 20)));
    }

    private static void AssertOnly(DirectionSector expected, IReadOnlyList<DirectionSector> actual) =>
        Assert.Equal(expected, Assert.Single(actual));

    [Fact]
    public void VerticalLineTestIncludesTheFootprintBorder()
    {
        var box = Place(Table, Vector3.Zero);

        Assert.True(box.IsCrossedByVerticalLine(new Vector3(50, 30, 100)));
        Assert.False(box.IsCrossedByVerticalLine(new Vector3(50.01f, 0, 100)));
    }
}
