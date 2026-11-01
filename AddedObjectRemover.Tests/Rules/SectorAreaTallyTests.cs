using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The surroundings rule both left-behind steps judge by: the state of each direction and the verdict over the directions.</summary>
public class SectorAreaTallyTests
{
    private const int HalfThreshold = 50;
    private const int Seed = 4711;
    private const int Trials = 300;
    private const int MostBoxes = 40;
    private const int MostPercent = 100;
    private const float Area = 10f;

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

        var beside = SectorAreaTally.Measure([(east, BoxFate.Removed), (west, BoxFate.Stands)], Vector3.Zero + new Vector3(0, 0, 200), HalfThreshold);
        var inside = SectorAreaTally.Measure([(around, BoxFate.Removed)], Vector3.Zero, HalfThreshold);

        Assert.Equal(SectorState.Removed, beside.State(DirectionSector.East));
        Assert.Equal(SectorState.Kept, beside.State(DirectionSector.West));
        Assert.Equal(CompassDirections.SectorCount, inside.RemovedCount);
    }

    [Fact]
    public void AClearedDirectionNamesWhetherRemovedRestoredOrBothBoxesClearedIt()
    {
        var tally = new SectorAreaTally(HalfThreshold);
        tally.Add(DirectionSector.East, 10, BoxFate.Removed);
        tally.Add(DirectionSector.East, 10, BoxFate.Restored);
        tally.Add(DirectionSector.West, 10, BoxFate.Restored);
        tally.Add(DirectionSector.North, 10, BoxFate.Removed);
        tally.Add(DirectionSector.South, 10, BoxFate.Stands);

        var areas = tally.Build();

        Assert.Equal(
            [SectorState.RemovedAndRestored, SectorState.Restored, SectorState.Removed, SectorState.Kept],
            new[] { DirectionSector.East, DirectionSector.West, DirectionSector.North, DirectionSector.South }.Select(areas.State));
        Assert.Equal((3, 2, 2), (areas.ClearedCount, areas.RemovedCount, areas.RestoredCount));
        Assert.Equal((10f, 10f), (areas.Removed(DirectionSector.East), areas.Restored(DirectionSector.East)));
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

    [Fact]
    public void ATallyThatAlreadyHoldsGoneBoxesCannotGiveTheAreasWithGone()
    {
        var tally = new SectorAreaTally(HalfThreshold);
        tally.Add(DirectionSector.North, Area, BoxFate.Removed);

        Assert.Throws<InvalidOperationException>(() => tally.WithGone([]));
    }

    [Fact]
    public void AreasWithGoneEqualAFullMeasurementBitForBit()
    {
        var random = new Random(Seed);
        for (var trial = 0; trial < Trials; trial++)
        {
            var point = RandomBoxes.Point(random);
            var percent = random.Next(1, MostPercent + 1);
            var boxes = RandomBoxes.Some(random, random.Next(0, MostBoxes));
            var fates = boxes.Select(_ => RandomFate(random)).ToArray();

            var allStanding = new SectorAreaTally(percent);
            foreach (var box in boxes) allStanding.Add(SectorFootprint.Of(point, box), BoxFate.Stands);
            var gone = boxes.Zip(fates).Where(pair => pair.Second != BoxFate.Stands).Select(pair => (SectorFootprint.Of(point, pair.First), pair.Second));

            var viaGone = allStanding.WithGone(gone);
            var measured = SectorAreaTally.Measure(boxes.Zip(fates), point, percent);

            Assert.Equal(Bits(measured.TotalAreas), Bits(viaGone.TotalAreas));
            Assert.Equal(Bits(measured.RemovedAreas), Bits(viaGone.RemovedAreas));
            Assert.Equal(Bits(measured.RestoredAreas), Bits(viaGone.RestoredAreas));
            Assert.Equal(measured.States.ToArray(), viaGone.States.ToArray());
        }
    }

    [Fact]
    public void WithGoneLeavesTheTallyUnchanged()
    {
        var tally = new SectorAreaTally(HalfThreshold);
        tally.Add(DirectionSector.North, Area, BoxFate.Stands);
        var before = tally.Build();

        var withGone = tally.WithGone([(new SectorFootprint(Area, DirectionSector.North), BoxFate.Removed)]);

        Assert.Equal(SectorState.Removed, withGone.State(DirectionSector.North));
        Assert.Equal(before.States.ToArray(), tally.Build().States.ToArray());
        Assert.Equal(Bits(before.RemovedAreas), Bits(tally.Build().RemovedAreas));
    }

    private static int[] Bits(IEnumerable<float> values) => [.. values.Select(BitConverter.SingleToInt32Bits)];

    private static BoxFate RandomFate(Random random) => (BoxFate)random.Next(Enum.GetValues<BoxFate>().Length);
}
