using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.CompassSectorAreas;

public class SectorFootprintTests
{
    private const int Seed = 4711;
    private const int Trials = 300;
    private const int MostBoxes = 40;
    private const float MostHalfSize = 120f;

    [Fact]
    public void FootprintAgreesWithTheDirectionsTheBoxCountsIn()
    {
        var random = new Random(Seed);
        for (var trial = 0; trial < Trials; trial++)
        {
            var point = RandomBoxes.Point(random);
            foreach (var box in RandomBoxes.Some(random, random.Next(1, MostBoxes)))
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
}
