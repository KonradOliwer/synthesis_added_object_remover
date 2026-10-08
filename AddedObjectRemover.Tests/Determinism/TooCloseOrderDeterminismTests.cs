using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>The too-close searches give the same hits and work counters whatever order the targets are walked in.</summary>
public class TooCloseOrderDeterminismTests
{
    private const int Threads = 8;
    private const float Multiplier = 1.5f;
    private const int TargetCount = 80;
    private const int OtherModObjectCount = 120;
    private const int InvisibleEvery = 5;
    private const int ObjectOtherModEvery = 3;
    private const float SceneExtent = 500f;
    private const int NpcHalfWidth = 15;
    private const int NpcHeight = 60;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("ClashOrder.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey Npc = new(Mod, 0x602);

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Boulder = new(
        new FormKey(Mod, 0x703), @"test\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50, -50, -20), new Vector3(50, 50, 40))));

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(Records, "ClashOrderData", Table, Boulder));

    private sealed record TooCloseScene(List<TargetObject> Targets, List<OtherObject> OtherModObjects);

    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject)]
    public void FindMatchesForReversedAndShuffledWorkOrders(ZoneShape zone, NpcHandling npcs)
    {
        var scene = CreateScene();
        var options = new TooCloseOptions(Multiplier, zone, npcs);
        var normal = TargetWorkOrder.Of(scene.Targets);

        var expected = Find(scene, options, normal);

        Assert.NotEmpty(expected.Hits);
        foreach (var order in ShuffledWorkOrders.Variants(normal))
        {
            var actual = Find(scene, options, order);
            Assert.Equal(expected.Hits.ToList(), actual.Hits.ToList());
            Assert.Equal(expected.Work, actual.Work);
            Assert.Equal(expected.LargeOtherObjects, actual.LargeOtherObjects);
            Assert.Equal(expected.Npc is null, actual.Npc is null);
            if (expected.Npc is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
        }
    }

    private static TooCloseResult Find(TooCloseScene scene, TooCloseOptions options, WorkOrder order)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var world = TestScenes.Create(scene.Targets, scene.OtherModObjects, Shapes, bodies: NewBodies(), threads: Threads);
        return TooCloseObjects.Find(
            new TooCloseInput(
                [.. scene.Targets],
                world.VisibleTargets,
                order,
                world.ObjectsThatCanCauseRemovals(replacements, options.Npcs),
                options.Npcs == NpcHandling.OnlyWhenStuckInObject ? world.NpcsThatCanSpawn(replacements) : null,
                Shapes,
                new TriangleStore(Shapes.ReadTriangles),
                new Execution(Threads)),
            options);
    }

    private static TooCloseScene CreateScene()
    {
        var random = new Random(73);
        BaseKey[] targetBases = [Table.Base, Boulder.Base, TestVisibility.TaggedBase(ObjectVisibility.Visible)];
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = Enumerable.Range(0, TargetCount)
            .Select(i => TestTargets.Create(
                i,
                TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)),
                i % InvisibleEvery == 0 ? xMarker : targetBases[i % targetBases.Length],
                TestTargets.Space))
            .ToList();
        var otherModObjects = Enumerable.Range(0, OtherModObjectCount)
            .Select(i => i % ObjectOtherModEvery == 0
                ? TestShapes.Placed(Mod, i, Table.Base, RandomGroundPoint(random), TestMeshes.RandomAngle(random))
                : TestNpcs.Place(Mod, i, Npc, RandomGroundPoint(random), new Vector3(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();
        return new TooCloseScene(targets, otherModObjects);
    }

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static string Describe(NpcStuckSummary summary) =>
        $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}";

    private static NpcBodyCache NewBodies() =>
        TestNpcBodies.Create(Records.ToImmutableLinkCache(), Shapes);

    private static SkyrimMod CreateRecords()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Races.Add(TestNpcs.CreateRace(Race, skin: null, playable: false, armorRace: null));
        var npc = TestNpcs.CreateNpc(Npc, Race, female: false, template: null);
        npc.ObjectBounds = new ObjectBounds
        {
            First = new P3Int16(-NpcHalfWidth, -NpcHalfWidth, 0),
            Second = new P3Int16(NpcHalfWidth, NpcHalfWidth, NpcHeight),
        };
        mod.Npcs.Add(npc);
        return mod;
    }
}
