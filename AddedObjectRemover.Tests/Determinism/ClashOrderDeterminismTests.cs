using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>The too-close searches give the same hits and work counters whatever order the targets are walked in.</summary>
public class ClashOrderDeterminismTests
{
    private const int Threads = 8;
    private const float Multiplier = 1.5f;
    private const int TargetCount = 80;
    private const int RivalCount = 120;
    private const int InvisibleEvery = 5;
    private const int ObjectRivalEvery = 3;
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
    private static readonly ShapeCatalog Shapes = TestShapes.Create(Records, "ClashOrderData", Table, Boulder);

    private sealed record ClashScene(List<TargetObject> Targets, TargetLooks Looks, List<OtherObject> Rivals);

    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject)]
    public void FindMatchesForReversedAndShuffledWorkOrders(ZoneShape zone, NpcHandling npcs)
    {
        var scene = CreateScene();
        var options = new ClashOptions(Multiplier, zone, npcs);
        var normal = WorkOrder.Of(scene.Targets);

        var expected = Find(scene, options, normal);

        Assert.NotEmpty(expected.Hits);
        foreach (var order in ShuffledWorkOrders.Variants(normal))
        {
            var actual = Find(scene, options, order);
            Assert.Equal(expected.Hits.ToList(), actual.Hits.ToList());
            Assert.Equal(expected.Work, actual.Work);
            Assert.Equal(expected.LargeRivals, actual.LargeRivals);
            Assert.Equal(expected.Npc is null, actual.Npc is null);
            if (expected.Npc is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
        }
    }

    private static ClashResult Find(ClashScene scene, ClashOptions options, WorkOrder order)
    {
        var replacements = Replacements.None(scene.Rivals.Count);
        var world = TestScenes.Create(scene.Targets, scene.Rivals, Shapes, bodies: NewBodies(), threads: Threads);
        return Clashes.Find(
            new ClashInput(
                [.. scene.Targets],
                scene.Looks,
                order,
                world.ActiveRivals(replacements, options.Npcs),
                options.Npcs == NpcHandling.OnlyWhenStuckInObject ? world.Npcs(replacements) : null,
                Shapes,
                new TriangleStore(Shapes.ReadGeometry),
                new Execution(Threads),
                UntimedPhases.Instance),
            options);
    }

    private static ClashScene CreateScene()
    {
        var random = new Random(73);
        BaseRef?[] targetBases = [Table.Ref, Boulder.Ref, null];
        var targets = Enumerable.Range(0, TargetCount)
            .Select(i => TestTargets.Create(i, TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)), targetBases[i % targetBases.Length], TestTargets.Space))
            .ToList();
        var looks = new TargetLooks([.. Enumerable.Range(0, TargetCount)
            .Select(i => i % InvisibleEvery == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers) : ObjectVisibility.Visible)]);
        var rivals = Enumerable.Range(0, RivalCount)
            .Select(i => i % ObjectRivalEvery == 0
                ? TestShapes.Placed(Mod, i, Table.Ref, RandomGroundPoint(random), TestMeshes.RandomAngle(random))
                : TestNpcs.Place(Mod, i, Npc, RandomGroundPoint(random), new P3Float(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();
        return new ClashScene(targets, looks, rivals);
    }

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static string Describe(NpcStuckSummary summary) =>
        $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}";

    private static NpcBodyCache NewBodies() =>
        new(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, new SkinnedBodyMeasurer(Shapes.ReadGeometry)));

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
