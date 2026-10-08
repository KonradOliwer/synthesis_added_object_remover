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
        Assert.Equal(Enum.Parse<DirectionSector>(expected), CompassDirections.Of(new Vector2(x, y)));

    [Theory]
    [InlineData(22.4f, nameof(DirectionSector.East))]
    [InlineData(22.6f, nameof(DirectionSector.NorthEast))]
    [InlineData(337.4f, nameof(DirectionSector.SouthEast))]
    [InlineData(337.6f, nameof(DirectionSector.East))]
    public void SectorsSpanHalfASectorEachSide(float degrees, string expected)
    {
        var radians = float.DegreesToRadians(degrees);
        Assert.Equal(Enum.Parse<DirectionSector>(expected), CompassDirections.Of(new Vector2(MathF.Cos(radians), MathF.Sin(radians))));
    }

    [Fact]
    public void DirectionIsRemovedAtTheAreaThreshold()
    {
        var atThreshold = new SectorAreaTally(HalfThreshold);
        atThreshold.Add(DirectionSector.North, 50, removed: true);
        atThreshold.Add(DirectionSector.North, 50, removed: false);
        Assert.Equal(SectorState.Removed, atThreshold.Build().State(DirectionSector.North));

        var below = new SectorAreaTally(HalfThreshold);
        below.Add(DirectionSector.North, 49, removed: true);
        below.Add(DirectionSector.North, 51, removed: false);
        var belowAreas = below.Build();
        Assert.Equal(SectorState.Kept, belowAreas.State(DirectionSector.North));
        Assert.Equal(SectorState.Empty, belowAreas.State(DirectionSector.South));
    }

    [Fact]
    public void ZeroAreaDirectionIsJudgedByObjectCount()
    {
        var half = new SectorAreaTally(HalfThreshold);
        half.Add(DirectionSector.East, 0, removed: true);
        half.Add(DirectionSector.East, 0, removed: false);
        Assert.Equal(SectorState.Removed, half.Build().State(DirectionSector.East));

        var third = new SectorAreaTally(HalfThreshold);
        third.Add(DirectionSector.East, 0, removed: true);
        third.Add(DirectionSector.East, 0, removed: false);
        third.Add(DirectionSector.East, 0, removed: false);
        Assert.Equal(SectorState.Kept, third.Build().State(DirectionSector.East));
    }

    [Fact]
    public void CountsOccupiedAndRemovedDirections()
    {
        var tally = new SectorAreaTally(HalfThreshold);
        tally.Add(DirectionSector.East, 10, removed: true);
        tally.Add(DirectionSector.West, 10, removed: false);
        tally.Add(DirectionSector.North, 10, removed: true);
        var areas = tally.Build();
        Assert.Equal(3, areas.OccupiedCount);
        Assert.Equal(2, areas.RemovedCount);
        Assert.StartsWith("East 10/10, NorthEast -, North 10/10, NorthWest -, West 0/10", SectorAreasText.Describe(areas));
    }
}
