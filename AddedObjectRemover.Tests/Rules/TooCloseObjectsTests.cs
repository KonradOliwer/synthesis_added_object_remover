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
    private const float Distance = 40f;
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
        var settings = new TooCloseSettings(BySize: true, Multiplier, ByDistance: false, DistanceInUnits: 0f, zone, npcs);

        var expected = FindWithSearches(scene, settings);
        var actual = FindWithFacade(scene, settings);

        Assert.NotEmpty(expected.Search.Hits);
        Assert.Equal(expected.Search.Hits, actual.Hits);
        Assert.Equal(expected.Search.Work, actual.Work);
        Assert.Equal(expected.Search.Hits.Select(hit => hit.TargetIndex), actual.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(expected.LargeOtherObjects, actual.LargeOtherObjects);
        Assert.Equal(expected.Summary is null, actual.Npc is null);
        if (expected.Summary is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void FindIsSwitchedOffAndLooksAtNothingWhenNoZoneIsOn(ZoneShape zone)
    {
        var settings = new TooCloseSettings(BySize: false, Multiplier, ByDistance: false, DistanceInUnits: Distance, zone, NpcHandling.OnlyWhenStuckInObject);

        var result = FindWithFacade(CreateScene(), settings);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Hits);
        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal(default, result.Work);
        Assert.Null(result.Npc);
        Assert.Equal(0, result.LargeOtherObjects);
    }

    [Theory]
    [InlineData(ZoneShape.BoundingBox)]
    [InlineData(ZoneShape.ObjectShape)]
    public void AStuckNpcIsFoundByTheObjectsOwnSizeWhicheverZonesAreOn(ZoneShape zone)
    {
        var settings = new TooCloseSettings(BySize: false, Multiplier, ByDistance: true, DistanceInUnits: 0f, zone, NpcHandling.OnlyWhenStuckInObject);

        var result = FindWithFacade(CreateScene(), settings);

        var npcHits = result.Hits.Where(hit => hit.TooCloseTo.IsPlacedNpc).ToList();
        Assert.NotEmpty(npcHits);
        Assert.All(npcHits, hit => Assert.Equal(new TooCloseCause.ObjectSize(), hit.Cause));
        Assert.All(result.Hits.Where(hit => !hit.TooCloseTo.IsPlacedNpc), hit => Assert.Equal(new TooCloseCause.Distance(0f), hit.Cause));
    }

    [Fact]
    public void TheResultNamesTheZonesItTestedInOrder()
    {
        var both = new TooCloseSettings(BySize: true, Multiplier, ByDistance: true, Distance, ZoneShape.BoundingBox, NpcHandling.Ignore);

        var result = FindWithFacade(CreateScene(), both);

        TooCloseCause[] expected = [new TooCloseCause.ObjectSize(), new TooCloseCause.Distance(Distance)];
        Assert.Equal(expected, result.ZonesTested.ToArray());
        Assert.Empty(FindWithFacade(CreateScene(), both with { BySize = false, ByDistance = false }).ZonesTested);
    }

    /// <summary>A target without mesh triangles uses its box in each zone it is tested with, and still counts once.</summary>
    [Fact]
    public void WithBothZonesOnEachTargetWithoutMeshTrianglesCountsOnce()
    {
        var scene = CreateScene();
        var sizeOnly = new TooCloseSettings(BySize: true, Multiplier: 0f, ByDistance: false, Distance, ZoneShape.ObjectShape, NpcHandling.Ignore);
        var withoutMesh = TestVisibility.TaggedBase(ObjectVisibility.Visible);

        var both = FindWithFacade(scene, sizeOnly with { ByDistance = true }).Work.Zone;

        Assert.Equal(scene.Targets.Count(target => target.Base == withoutMesh), both.BoxZoneTargets);
        Assert.Equal(FindWithFacade(scene, sizeOnly).Work.Zone.BoxZoneTargets, both.BoxZoneTargets);
    }

    private static (TooCloseSearchResult Search, int LargeOtherObjects, NpcStuckSummary? Summary) FindWithSearches(TooCloseScene scene, TooCloseSettings settings)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var world = CreateWorld(scene);
        var otherModObjects = world.ObjectsThatCanCauseRemovals(replacements, settings.Npcs);
        var npcRule = NpcTooCloseRule.Create(
            settings.Npcs,
            () => NpcStuckSearch.Create(scene.Targets, world.VisibleTargets, world.NpcsThatCanSpawn(replacements), Shapes, NewTriangles()));
        var order = TargetWorkOrder.Of(scene.Targets);
        var execution = new Execution(Threads);
        var largeOtherObjects = 0;
        var search = settings.Zone switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                scene.Targets, otherModObjects, Shapes, ZoneGrowth.Active(settings), npcRule, order, execution),
            _ => FindInShapeZone(scene, settings, world.VisibleTargets, otherModObjects, npcRule, order, execution, out largeOtherObjects),
        };
        return (search, largeOtherObjects, npcRule.StuckSearch?.GetSummary(search.Work.Npcs));
    }

    private static TooCloseSearchResult FindInShapeZone(
        TooCloseScene scene, TooCloseSettings settings, IVisibleTargetObjects visibleTargets, IObjectsThatCanCauseRemovals otherModObjects, NpcTooCloseRule npcRule, WorkOrder order, Execution execution, out int largeOtherObjects)
    {
        var zone = ShapeZoneSearch.Create(scene.Targets, visibleTargets, otherModObjects, Shapes, NewTriangles(), ZoneGrowth.Active(settings), npcRule);
        largeOtherObjects = zone.LargeOtherObjects;
        return zone.FindTooCloseTargets(order, execution);
    }

    private static TooCloseResult FindWithFacade(TooCloseScene scene, TooCloseSettings settings)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var caches = CreateWorld(scene);
        return TooCloseObjects.Find(
            TestScenes.CreateWorld(scene.Targets, scene.OtherModObjects),
            caches.VisibleTargets,
            caches.ObjectsThatCanCauseRemovals(replacements, settings.Npcs),
            caches.PlacedNpcs,
            replacements,
            Shapes,
            NewTriangles(),
            new Execution(Threads),
            settings);
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
