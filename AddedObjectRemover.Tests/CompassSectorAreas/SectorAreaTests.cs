using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.CompassSectorAreas;

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
        Assert.Equal(10, areas.Total(DirectionSector.East));
        Assert.Equal(10, areas.Removed(DirectionSector.East));
        Assert.Equal(10, areas.Total(DirectionSector.West));
        Assert.Equal(0, areas.Removed(DirectionSector.West));
    }

    [Fact]
    public void MeasureAddsEachBoxToTheDirectionsItCountsIn()
    {
        var east = OrientedBox.FromLocal(new Box(new Vector3(-5), new Vector3(5)), TestTargets.At(new Vector3(100, 0, 0), 0f));
        var west = OrientedBox.FromLocal(new Box(new Vector3(-5), new Vector3(5)), TestTargets.At(new Vector3(-100, 0, 0), 0f));
        var around = OrientedBox.FromLocal(new Box(new Vector3(-50), new Vector3(50)), TestTargets.At(Vector3.Zero, 0f));

        var beside = SectorAreaTally.Measure([(east, true), (west, false)], Vector3.Zero + new Vector3(0, 0, 200), HalfThreshold);
        var inside = SectorAreaTally.Measure([(around, true)], Vector3.Zero, HalfThreshold);

        Assert.Equal(SectorState.Removed, beside.State(DirectionSector.East));
        Assert.Equal(SectorState.Kept, beside.State(DirectionSector.West));
        Assert.Equal(CompassDirections.SectorCount, inside.RemovedCount);
    }

    [Theory]
    [InlineData(3, 3, 60, nameof(SectorVerdict.TooFewOccupiedDirections))]
    [InlineData(4, 3, 60, nameof(SectorVerdict.Cleared))]
    [InlineData(4, 2, 60, nameof(SectorVerdict.TooFewClearedDirections))]
    [InlineData(8, 7, 100, nameof(SectorVerdict.TooFewClearedDirections))]
    [InlineData(8, 8, 100, nameof(SectorVerdict.Cleared))]
    public void RuleNeedsEnoughOccupiedAndClearedDirections(int occupied, int cleared, int clearedPercent, string expected)
    {
        var tally = new SectorAreaTally(HalfThreshold);
        foreach (var sector in CompassDirections.All.Take(occupied)) tally.Add(sector, 10, removed: (int)sector < cleared);

        Assert.Equal(Enum.Parse<SectorVerdict>(expected), SectorRule.Decide(tally.Build(), minOccupiedDirections: 4, clearedPercent));
    }
}
