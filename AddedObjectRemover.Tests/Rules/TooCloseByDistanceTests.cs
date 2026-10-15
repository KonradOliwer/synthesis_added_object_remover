using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The distance zone of the too-close step: a target object is removed when another mod's object comes within a number of
/// game units of it, in both removal zones; with both zones on, an object found by both is removed once, with the size reason.
/// Every target is a 40 x 40 x 10 table, and each other-mod table stands to the east of its target at the given centre offset.
/// </summary>
public class TooCloseByDistanceTests
{
    private const float Spacing = 1000f;
    private const float Distance = 32f;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Distance.esp");

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\distance_table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Mod, "TooCloseByDistanceData", Table);

    /// <summary>Each offset is the east-west distance between the centres of a target table and the other-mod table beside it.</summary>
    private static TooCloseResult Find(TooCloseSettings settings, params float[] offsets)
    {
        var targets = Enumerable.Range(0, offsets.Length)
            .Select(i => TestTargets.Create(i, TestTargets.At(new Vector3(i * Spacing, 0, 0)), Table.Base, TestTargets.Space))
            .ToList();
        var others = Enumerable.Range(0, offsets.Length)
            .Select(i => TestShapes.Placed(Mod, i, Table.Base, new Vector3(i * Spacing + offsets[i], 0, 0)))
            .ToList();
        var world = TestScenes.Create(targets, others, Shapes);
        var replacements = Replacements.None(others.Count);
        return TooCloseObjects.Find(
            new TooCloseInput(
                [.. targets],
                world.VisibleTargets,
                TargetWorkOrder.Of(targets),
                world.ObjectsThatCanCauseRemovals(replacements, NpcHandling.Ignore),
                world.NpcsThatCanSpawn(replacements),
                Shapes,
                new TriangleStore(Shapes.ReadTriangles),
                new Execution(1)),
            settings);
    }

    private static TooCloseSettings DistanceOnly(ZoneShape zone, float units) =>
        new(BySize: false, Multiplier: 0f, ByDistance: true, units, zone, NpcHandling.Ignore);

    private static TooCloseSettings BothZones(ZoneShape zone, float multiplier) =>
        new(BySize: true, multiplier, ByDistance: true, Distance, zone, NpcHandling.Ignore);

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void TheDistanceZoneRemovesAnObjectWithinTheDistanceAndKeepsOneBeyondIt(ZoneShape zone)
    {
        const float within = 45f;
        const float beyond = 100f;

        var result = Find(DistanceOnly(zone, Distance), within, beyond);

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(0, hit.TargetIndex);
        Assert.Equal(new TooCloseCause.Distance(Distance), hit.Cause);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(new TargetId(0), proposal.Target);
        Assert.Equal(new RemovalReason.TooClose(hit.TooCloseTo.Id, new TooCloseCause.Distance(Distance)), proposal.Reason);
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void TheDistanceZoneFindsNothingWhenEveryOtherObjectIsFarAway(ZoneShape zone)
    {
        var result = Find(DistanceOnly(zone, Distance), 200f, 500f);

        Assert.Empty(result.Hits);
        Assert.Empty(result.Proposals);
    }

    /// <summary>The shape zone measures from the surface of the target's mesh; the box zone looks at the other object's centre only.</summary>
    [Theory]
    [InlineData(39f, 0f, true)]
    [InlineData(41f, 0f, false)]
    [InlineData(41f, 2f, true)]
    [InlineData(50f, 5f, false)]
    [InlineData(50f, 16f, true)]
    public void TheShapeZoneMeasuresTheDistanceFromTheTargetsMeshInWorldUnits(float offset, float units, bool expectedHit)
    {
        var result = Find(DistanceOnly(ZoneShape.ObjectShape, units), offset);

        Assert.Equal(expectedHit, result.Hits.Length == 1);
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void AnObjectFoundByBothZonesIsRemovedOnceWithTheSizeReason(ZoneShape zone)
    {
        var result = Find(BothZones(zone, multiplier: 0.5f), 30f);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(new TooCloseCause.ObjectSize(), hit.Cause);
        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(new RemovalReason.TooClose(hit.TooCloseTo.Id, new TooCloseCause.ObjectSize()), proposal.Reason);
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void WithBothZonesOnEachObjectGetsTheReasonOfTheZoneThatFoundIt(ZoneShape zone)
    {
        var result = Find(BothZones(zone, multiplier: 0.1f), 15f, 45f, 200f);

        Assert.Equal([0, 1], result.Hits.Select(hit => hit.TargetIndex));
        TooCloseCause[] causes = [new TooCloseCause.ObjectSize(), new TooCloseCause.Distance(Distance)];
        Assert.Equal(causes, result.Hits.Select(hit => hit.Cause));
        Assert.Equal(causes, result.Proposals.Select(proposal => Assert.IsType<RemovalReason.TooClose>(proposal.Reason).Cause));
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void TheSizeZoneAloneNeverUsesTheDistance(ZoneShape zone)
    {
        var settings = new TooCloseSettings(BySize: true, Multiplier: 0.1f, ByDistance: false, Distance, zone, NpcHandling.Ignore);

        var result = Find(settings, 45f);

        Assert.Empty(result.Hits);
    }
}
