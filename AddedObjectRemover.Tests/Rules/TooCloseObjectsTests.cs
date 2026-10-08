using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The TooCloseObjects facade gives the same hits, work and NPC summary as the searches it wraps, for every zone and NPC setting.</summary>
public class TooCloseObjectsTests
{
    private const int Threads = 4;
    private const float Multiplier = 1.5f;
    private const int TargetCount = 60;
    private const int OtherModObjectCount = 90;
    private const int InvisibleEvery = 5;
    private const int ObjectOtherModEvery = 3;
    private const float SceneExtent = 600f;
    private const int NpcHalfWidth = 15;
    private const int NpcHeight = 60;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Clashes.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey Npc = new(Mod, 0x602);

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Boulder = new(
        new FormKey(Mod, 0x703), @"test\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50, -50, -20), new Vector3(50, 50, 40))));

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(Records, "ClashesData", Table, Boulder));

    private sealed record TooCloseScene(List<TargetObject> Targets, List<OtherObject> OtherModObjects);

    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInObject)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.Ignore)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.CountLikeObjects)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.Ignore)]
    public void FindGivesTheSameResultAsTheSearches(ZoneShape zone, NpcHandling npcs)
    {
        var scene = CreateScene();
        var options = new TooCloseOptions(Multiplier, zone, npcs);

        var expected = FindWithSearches(scene, options);
        var actual = FindWithFacade(scene, options);

        Assert.NotEmpty(expected.Search.Hits);
        Assert.Equal(expected.Search.Hits, actual.Hits);
        Assert.Equal(expected.Search.Work, actual.Work);
        Assert.Equal(expected.Search.Hits.Select(hit => hit.TargetIndex), actual.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(expected.LargeOtherObjects, actual.LargeOtherObjects);
        Assert.Equal(expected.Summary is null, actual.Npc is null);
        if (expected.Summary is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
    }

    private static (TooCloseSearchResult Search, int LargeOtherObjects, NpcStuckSummary? Summary) FindWithSearches(TooCloseScene scene, TooCloseOptions options)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var world = CreateWorld(scene);
        var otherModObjects = world.ObjectsThatCanCauseRemovals(replacements, options.Npcs);
        var npcRule = NpcTooCloseRule.Create(
            options.Npcs,
            () => NpcStuckSearch.Create(scene.Targets, world.VisibleTargets, world.NpcsThatCanSpawn(replacements), Shapes, NewTriangles()));
        var order = TargetWorkOrder.Of(scene.Targets);
        var execution = new Execution(Threads);
        var largeOtherObjects = 0;
        var search = options.Zone switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                scene.Targets, otherModObjects, Shapes, options.Multiplier, npcRule, order, execution),
            _ => FindInShapeZone(scene, options, world.VisibleTargets, otherModObjects, npcRule, order, execution, out largeOtherObjects),
        };
        return (search, largeOtherObjects, npcRule.StuckSearch?.GetSummary(search.Work.Npcs));
    }

    private static TooCloseSearchResult FindInShapeZone(
        TooCloseScene scene, TooCloseOptions options, IVisibleTargetObjects visibleTargets, IObjectsThatCanCauseRemovals otherModObjects, NpcTooCloseRule npcRule, WorkOrder order, Execution execution, out int largeOtherObjects)
    {
        var zone = ShapeZoneSearch.Create(scene.Targets, visibleTargets, otherModObjects, Shapes, NewTriangles(), options.Multiplier, npcRule);
        largeOtherObjects = zone.LargeOtherObjects;
        return zone.FindTooCloseTargets(order, execution);
    }

    private static TooCloseResult FindWithFacade(TooCloseScene scene, TooCloseOptions options)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var world = CreateWorld(scene);
        return TooCloseObjects.Find(
            new TooCloseInput(
                [.. scene.Targets],
                world.VisibleTargets,
                TargetWorkOrder.Of(scene.Targets),
                world.ObjectsThatCanCauseRemovals(replacements, options.Npcs),
                options.Npcs == NpcHandling.OnlyWhenStuckInObject ? world.NpcsThatCanSpawn(replacements) : null,
                Shapes,
                NewTriangles(),
                new Execution(Threads)),
            options);
    }

    /// <summary>Visible targets with and without meshes, every few of them invisible, among other mods' objects and placed NPCs.</summary>
    private static TooCloseScene CreateScene()
    {
        var random = new Random(52);
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

    private static ObjectCaches CreateWorld(TooCloseScene scene) =>
        TestScenes.Create(scene.Targets, scene.OtherModObjects, Shapes, bodies: NewBodies(), threads: Threads);

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static string Describe(NpcStuckSummary summary) =>
        $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}";

    private static TriangleStore NewTriangles() => new(Shapes.ReadTriangles);

    private static NpcBodyCache NewBodies() =>
        TestNpcBodies.Create(Records.ToImmutableLinkCache(), Shapes);

    /// <summary>Non-playable race without a skin, so the NPC is sized by its Object Bounds.</summary>
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
