using System.Numerics;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The reach the restore guards with must contain every zone the too-close rule grows, whatever the object's size, scale and the zone's growth.</summary>
public class TooCloseZoneReachTests
{
    private const float RoundingTolerance = 1e-3f;

    private static TooCloseSettings Settings(bool bySize, float multiplier, bool byDistance, float distance) =>
        new(bySize, multiplier, byDistance, distance, ZoneShape.BoundingBox, NpcHandling.Never);

    private static float CornerDistanceOfGrownBox(ZoneGrowth zone, Box local, float scale) => zone.GrownScaledBox(local, scale).HalfExtents.Length();

    [Theory]
    [InlineData(10f, 10f, 10f, 1f, 0f)]
    [InlineData(40f, 40f, 10f, 1f, 32f)]
    [InlineData(40f, 40f, 10f, 2.5f, 32f)]
    [InlineData(3f, 200f, 50f, 0.5f, 1000f)]
    [InlineData(1f, 2f, 3f, 4f, 0.5f)]
    public void AZoneGrownByDistanceLiesWithinTheReach(float sizeX, float sizeY, float sizeZ, float scale, float distance)
    {
        var local = new Box(-new Vector3(sizeX, sizeY, sizeZ) / 2f, new Vector3(sizeX, sizeY, sizeZ) / 2f);
        var reach = TooCloseZoneReach.ReachRadius(Settings(bySize: false, 0f, byDistance: true, distance), local.Scaled(scale).HalfExtents);

        var corner = CornerDistanceOfGrownBox(new ZoneGrowth.ByDistance(distance), local, scale);

        Assert.InRange(corner, 0f, reach + RoundingTolerance * reach);
    }

    [Theory]
    [InlineData(10f, 10f, 10f, 1f, 0f)]
    [InlineData(40f, 40f, 10f, 1f, 0.5f)]
    [InlineData(40f, 40f, 10f, 2.5f, 1f)]
    [InlineData(3f, 200f, 50f, 0.5f, 3f)]
    [InlineData(1f, 2f, 3f, 4f, 0.25f)]
    public void AZoneGrownBySizeLiesWithinTheReach(float sizeX, float sizeY, float sizeZ, float scale, float multiplier)
    {
        var local = new Box(-new Vector3(sizeX, sizeY, sizeZ) / 2f, new Vector3(sizeX, sizeY, sizeZ) / 2f);
        var reach = TooCloseZoneReach.ReachRadius(Settings(bySize: true, multiplier, byDistance: false, 0f), local.Scaled(scale).HalfExtents);

        var corner = CornerDistanceOfGrownBox(new ZoneGrowth.BySize(multiplier), local, scale);

        Assert.InRange(corner, 0f, reach + RoundingTolerance * reach);
    }

    [Fact]
    public void TheReachIsTheLargerOfTheTwoZonesAndNothingWhenNoZoneIsOn()
    {
        var half = new Vector3(20f, 20f, 5f);

        var bySize = TooCloseZoneReach.ReachRadius(Settings(bySize: true, 0.5f, byDistance: false, 0f), half);
        var byDistance = TooCloseZoneReach.ReachRadius(Settings(bySize: false, 0f, byDistance: true, 32f), half);

        Assert.Equal(MathF.Max(bySize, byDistance), TooCloseZoneReach.ReachRadius(Settings(bySize: true, 0.5f, byDistance: true, 32f), half));
        Assert.Equal(0f, TooCloseZoneReach.ReachRadius(Settings(bySize: false, 0f, byDistance: false, 0f), half));
    }
}
