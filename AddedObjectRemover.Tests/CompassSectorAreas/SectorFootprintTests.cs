using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.CompassSectorAreas;

public class SectorFootprintTests
{
    private const int Seed = 4711;
    private const int Trials = 300;
    private const int MostBoxes = 40;
    private const int MostPercent = 100;
    private const float Reach = 300f;
    private const float MostHalfSize = 120f;
    private const int Percent = 50;
    private const float Area = 10f;

    [Fact]
    public void FootprintAgreesWithTheDirectionsTheBoxCountsIn()
    {
        var random = new Random(Seed);
        for (var trial = 0; trial < Trials; trial++)
        {
            var point = RandomPoint(random);
            foreach (var box in RandomBoxes(random, random.Next(1, MostBoxes)))
            {
                var footprint = SectorFootprint.Of(point, box);

                Assert.Equal(box.Contains(point) ? CompassDirections.All : [CompassDirections.Towards(point, box)], footprint.Sectors);
                Assert.Equal(box.GroundArea, footprint.GroundArea);
                Assert.Equal(box.Contains(point), footprint.Sector is null);
            }
        }
    }

    [Fact]
    public void ABoxContainingThePointCountsInEveryDirection()
    {
        var box = OrientedBox.FromLocal(new Box(new Vector3(-MostHalfSize), new Vector3(MostHalfSize)), TestTargets.At(Vector3.Zero));

        var footprint = SectorFootprint.Of(Vector3.Zero, box);

        Assert.Null(footprint.Sector);
        Assert.Equal(CompassDirections.SectorCount, footprint.Sectors.Count);
    }

    [Fact]
    public void ATallyThatAlreadyHoldsGoneBoxesCannotGiveTheAreasWithGone()
    {
        var tally = new SectorAreaTally(Percent);
        tally.Add(DirectionSector.North, Area, BoxFate.Removed);

        Assert.Throws<InvalidOperationException>(() => tally.WithGone([]));
    }

    [Fact]
    public void AreasWithGoneEqualAFullMeasurementBitForBit()
    {
        var random = new Random(Seed);
        for (var trial = 0; trial < Trials; trial++)
        {
            var point = RandomPoint(random);
            var percent = random.Next(1, MostPercent + 1);
            var boxes = RandomBoxes(random, random.Next(0, MostBoxes));
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
        var tally = new SectorAreaTally(Percent);
        tally.Add(DirectionSector.North, Area, BoxFate.Stands);
        var before = tally.Build();

        var withGone = tally.WithGone([(new SectorFootprint(Area, DirectionSector.North), BoxFate.Removed)]);

        Assert.Equal(SectorState.Removed, withGone.State(DirectionSector.North));
        Assert.Equal(before.States.ToArray(), tally.Build().States.ToArray());
        Assert.Equal(Bits(before.RemovedAreas), Bits(tally.Build().RemovedAreas));
    }

    private static int[] Bits(IEnumerable<float> values) => [.. values.Select(BitConverter.SingleToInt32Bits)];

    private static BoxFate RandomFate(Random random) => (BoxFate)random.Next(Enum.GetValues<BoxFate>().Length);

    private static Vector3 RandomPoint(Random random) =>
        new(RandomOffset(random), RandomOffset(random), RandomOffset(random) / 4);

    private static OrientedBox[] RandomBoxes(Random random, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => RandomBox(random))];

    /// <summary>Some box sizes are zero, so that directions without ground area occur.</summary>
    private static OrientedBox RandomBox(Random random)
    {
        var half = new Vector3(RandomHalfSize(random), RandomHalfSize(random), RandomHalfSize(random));
        var position = new Vector3(RandomOffset(random), RandomOffset(random), RandomOffset(random) / 4);
        return OrientedBox.FromLocal(new Box(-half, half), TestTargets.At(position, random.NextSingle() * MathF.Tau));
    }

    private static float RandomHalfSize(Random random) => random.Next(5) == 0 ? 0f : random.NextSingle() * MostHalfSize;

    private static float RandomOffset(Random random) => (random.NextSingle() * 2 - 1) * Reach;
}
