using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The Clashes facade gives the same hits, work and NPC summary as the searches it wraps, for every zone and NPC setting.</summary>
public class ClashesTests
{
    private const int Threads = 4;
    private const float Multiplier = 1.5f;
    private const int TargetCount = 60;
    private const int RivalCount = 90;
    private const int InvisibleEvery = 5;
    private const int ObjectRivalEvery = 3;
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
    private static readonly ShapeCatalog Shapes = TestShapes.Create(Records, "ClashesData", Table, Boulder);

    private sealed record ClashScene(List<TargetObject> Targets, TargetLooks Looks, List<OtherObject> Rivals);

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
        var options = new ClashOptions(Multiplier, zone, npcs);

        var expected = FindWithSearches(scene, options);
        var actual = FindWithFacade(scene, options);

        Assert.NotEmpty(expected.Search.Hits);
        Assert.Equal(expected.Search.Hits, actual.Hits);
        Assert.Equal(expected.Search.Work, actual.Work);
        Assert.Equal(expected.Search.Hits.Select(hit => hit.TargetIndex), actual.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(expected.LargeRivals, actual.LargeRivals);
        Assert.Equal(expected.Summary is null, actual.Npc is null);
        if (expected.Summary is { } summary) Assert.Equal(Describe(summary), Describe(actual.Npc!));
    }

    [Fact]
    public void FindTimesTheShapeZoneIndexAndSearch()
    {
        var clock = new PhaseClock();

        FindWithFacade(CreateScene(), new ClashOptions(Multiplier, ZoneShape.ObjectShape, NpcHandling.Ignore), clock);

        Assert.Single(clock.Times().Samples(TimedPhase.ShapeZoneIndexBuild));
        Assert.Single(clock.Times().Samples(TimedPhase.ShapeZoneSearch));
    }

    private static (ClashSearchResult Search, int LargeRivals, NpcStuckSummary? Summary) FindWithSearches(ClashScene scene, ClashOptions options)
    {
        var replacements = Replacements.None(scene.Rivals.Count);
        var world = CreateWorld(scene);
        var rivals = world.ActiveRivals(replacements, options.Npcs);
        var npcRule = NpcClashRule.Create(
            options.Npcs,
            () => NpcStuckSearch.Create(scene.Targets, scene.Looks, world.Npcs(replacements), Shapes, NewTriangles()));
        var order = WorkOrder.Of(scene.Targets);
        var execution = new Execution(Threads);
        var largeRivals = 0;
        var search = options.Zone switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                scene.Targets, scene.Looks, rivals, Shapes, options.Multiplier, npcRule, order, execution),
            _ => FindInShapeZone(scene, options, rivals, npcRule, order, execution, out largeRivals),
        };
        return (search, largeRivals, npcRule.StuckSearch?.GetSummary(search.Work.Npcs));
    }

    private static ClashSearchResult FindInShapeZone(
        ClashScene scene, ClashOptions options, IActiveRivals rivals, NpcClashRule npcRule, WorkOrder order, Execution execution, out int largeRivals)
    {
        var zone = ShapeZoneSearch.Create(scene.Targets, scene.Looks, rivals, Shapes, NewTriangles(), options.Multiplier, npcRule);
        largeRivals = zone.LargeOtherObjects;
        return zone.FindTooCloseTargets(scene.Looks, order, execution);
    }

    private static ClashResult FindWithFacade(ClashScene scene, ClashOptions options, IPhaseTimer? timer = null)
    {
        var replacements = Replacements.None(scene.Rivals.Count);
        var world = CreateWorld(scene);
        return Clashes.Find(
            new ClashInput(
                [.. scene.Targets],
                scene.Looks,
                WorkOrder.Of(scene.Targets),
                world.ActiveRivals(replacements, options.Npcs),
                options.Npcs == NpcHandling.OnlyWhenStuckInObject ? world.Npcs(replacements) : null,
                Shapes,
                NewTriangles(),
                new Execution(Threads),
                timer ?? UntimedPhases.Instance),
            options);
    }

    /// <summary>Visible targets with and without meshes, every few of them invisible, among other mods' objects and placed NPCs.</summary>
    private static ClashScene CreateScene()
    {
        var random = new Random(52);
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

    private static Scene CreateWorld(ClashScene scene) =>
        TestScenes.Create(scene.Targets, scene.Rivals, Shapes, bodies: NewBodies(), threads: Threads);

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static string Describe(NpcStuckSummary summary) =>
        $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}";

    private static TriangleStore NewTriangles() => new(Shapes.ReadGeometry);

    private static NpcBodyCache NewBodies() =>
        new(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, new SkinnedBodyMeasurer(Shapes.ReadGeometry)));

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
