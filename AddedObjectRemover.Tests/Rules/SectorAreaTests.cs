using System.Numerics;

namespace AddedObjectRemover.Tests.Rules;

public class SectorAreaTests
{
    private const int HalfThreshold = 50;

    [Theory]
    [InlineData(1f, 0f, nameof(DirectionSector.East))]
    [InlineData(1f, 1f, nameof(DirectionSector.NorthEast))]
    [InlineData(0f, 1f, nameof(DirectionSector.North))]
    [InlineData(-1f, 1f, nameof(DirectionSector.NorthWest))]
    [InlineData(-1f, 0f, nameof(DirectionSector.West))]
    [InlineData(-1f, -0.0001f, nameof(DirectionSector.West))]
    [InlineData(-1f, -1f, nameof(DirectionSector.SouthWest))]
    [InlineData(0f, -1f, nameof(DirectionSector.South))]
    [InlineData(1f, -1f, nameof(DirectionSector.SouthEast))]
    [InlineData(1f, -0.0001f, nameof(DirectionSector.East))]
    [InlineData(0f, 0f, nameof(DirectionSector.East))]
    public void OffsetFallsInItsCompassSector(float x, float y, string expected) =>
        Assert.Equal(Enum.Parse<DirectionSector>(expected), SectorAreas.SectorOf(new Vector2(x, y)));

    [Theory]
    [InlineData(22.4f, nameof(DirectionSector.East))]
    [InlineData(22.6f, nameof(DirectionSector.NorthEast))]
    [InlineData(337.4f, nameof(DirectionSector.SouthEast))]
    [InlineData(337.6f, nameof(DirectionSector.East))]
    public void SectorsSpanHalfASectorEachSide(float degrees, string expected)
    {
        var radians = float.DegreesToRadians(degrees);
        Assert.Equal(Enum.Parse<DirectionSector>(expected), SectorAreas.SectorOf(new Vector2(MathF.Cos(radians), MathF.Sin(radians))));
    }

    [Fact]
    public void DirectionIsRemovedAtTheAreaThreshold()
    {
        var atThreshold = new SectorAreas(HalfThreshold);
        atThreshold.Add(DirectionSector.North, 50, removed: true);
        atThreshold.Add(DirectionSector.North, 50, removed: false);
        Assert.Equal(SectorState.Removed, atThreshold.State(DirectionSector.North));

        var below = new SectorAreas(HalfThreshold);
        below.Add(DirectionSector.North, 49, removed: true);
        below.Add(DirectionSector.North, 51, removed: false);
        Assert.Equal(SectorState.Kept, below.State(DirectionSector.North));
        Assert.Equal(SectorState.Empty, below.State(DirectionSector.South));
    }

    [Fact]
    public void ZeroAreaDirectionIsJudgedByObjectCount()
    {
        var half = new SectorAreas(HalfThreshold);
        half.Add(DirectionSector.East, 0, removed: true);
        half.Add(DirectionSector.East, 0, removed: false);
        Assert.Equal(SectorState.Removed, half.State(DirectionSector.East));

        var third = new SectorAreas(HalfThreshold);
        third.Add(DirectionSector.East, 0, removed: true);
        third.Add(DirectionSector.East, 0, removed: false);
        third.Add(DirectionSector.East, 0, removed: false);
        Assert.Equal(SectorState.Kept, third.State(DirectionSector.East));
    }

    [Fact]
    public void CountsOccupiedAndRemovedDirections()
    {
        var areas = new SectorAreas(HalfThreshold);
        areas.Add(DirectionSector.East, 10, removed: true);
        areas.Add(DirectionSector.West, 10, removed: false);
        areas.Add(DirectionSector.North, 10, removed: true);
        Assert.Equal(3, areas.OccupiedCount);
        Assert.Equal(2, areas.RemovedCount);
        Assert.StartsWith("East 10/10, NorthEast -, North 10/10, NorthWest -, West 0/10", areas.Describe());
    }
}
