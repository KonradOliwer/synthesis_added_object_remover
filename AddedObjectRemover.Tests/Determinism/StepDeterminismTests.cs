using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>
/// The follow-up loops, the too-close searches and the leftover selection give the same results and
/// work counts with one and with eight threads, on synthetic scenes large enough to span many parallel batches.
/// </summary>
public class StepDeterminismTests
{
    private const int SequentialThreads = 1;
    private const int ParallelThreads = 8;
    private const float TouchDistance = 1f;
    private const int GridSize = 10;
    private const float GridSpacing = 41f;
    private const float GridJitter = 2f;
    private const int LinkCount = 12;
    private const int SeedCount = 4;
    private const int KeptCount = 3;
    private const float SceneExtent = 1000f;
    private const int NpcTargetCount = 30;
    private const int PlacedNpcCount = 150;
    private const int ShapeZoneTargetCount = 80;
    private const int ShapeZoneOtherModObjectCount = 120;
    private const int InvisibleEvery = 5;
    private const int ObjectOtherModEvery = 4;
    private const float ShapeZoneMultiplier = 1.5f;
    private const int MarkerCount = 120;
    private const int RoomCount = 4;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Scene.esp");
    private static readonly FormKey Race = new(Mod, 0x601);
    private static readonly FormKey SmallNpc = new(Mod, 0x602);
    private static readonly FormKey LargeNpc = new(Mod, 0x603);
    private static readonly FormKey LeveledTemplateNpc = new(Mod, 0x604);
    private static readonly FormKey Leveled = new(Mod, 0x605);
    private static readonly FormKey[] NpcBases = [SmallNpc, LargeNpc, LeveledTemplateNpc];

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Item = new(
        new FormKey(Mod, 0x702), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic Boulder = new(
        new FormKey(Mod, 0x703), @"test\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50, -50, -20), new Vector3(50, 50, 40))));

    /// <summary>One room without a floor: an open mesh.</summary>
    private static readonly TestStatic Building = new(
        new FormKey(Mod, 0x704), @"test\building.nif",
        TestMeshes.BoxWithoutFace(new Box(new Vector3(-100, -100, 0), new Vector3(100, 100, 150)), v => v.Z == 0));

    private static readonly SkyrimMod Records = CreateRecords();
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(Records, "StepDeterminismData", Table, Item, Boulder, Building));

    private sealed record RestingObjectsScene(List<TargetObject> Targets, List<int> Seeds, ObjectsToKeep Protection);

    private sealed record ShapeZoneScene(List<TargetObject> Targets, List<OtherObject> OtherModObjects);

    [Fact]
    public void EverythingTouchingMatchesForOneAndEightThreads()
    {
        var scene = CreateRestingObjectsScene();

        var sequential = RunTouchCascade(scene, SequentialThreads);
        var parallel = RunTouchCascade(scene, ParallelThreads);

        Assert.NotEqual(0, RestingObjectsRemovals.CountByRule(sequential.Result));
        Assert.Contains(
            sequential.AlsoRemoveRounds.SelectMany(round => sequential.RemovalDecisions.RemovedIn(round)),
            target => sequential.RemovalDecisions.Of(target)!.Reason is RemovalReason.LinkedTo);
        Assert.Equal(DescribeRounds(sequential.RemovalDecisions), DescribeRounds(parallel.RemovalDecisions));
        Assert.Equal(sequential.RemovalDecisions.All().ToList(), parallel.RemovalDecisions.All().ToList());
        Assert.Equal(sequential.Result.Work, parallel.Result.Work);
        Assert.Equal(DescribeStats(sequential.TouchChains.Stats), DescribeStats(parallel.TouchChains.Stats));
        Assert.Equal(DescribeComponents(sequential.Explanation!), DescribeComponents(parallel.Explanation!));
    }

    [Fact]
    public void AnchoringMatchesForOneAndEightThreads()
    {
        var scene = CreateRestingObjectsScene();

        var sequential = RunSupportCascade(scene, SequentialThreads);
        var parallel = RunSupportCascade(scene, ParallelThreads);

        Assert.True(sequential.Result.Work.Rounds > 1);
        Assert.Equal(DescribeRounds(sequential.RemovalDecisions), DescribeRounds(parallel.RemovalDecisions));
        Assert.Equal(sequential.RemovalDecisions.All().ToList(), parallel.RemovalDecisions.All().ToList());
        Assert.Equal(DescribeEvaluations(sequential.Result), DescribeEvaluations(parallel.Result));
        Assert.Equal(sequential.Result.Work, parallel.Result.Work);
    }

    [Fact]
    public void NpcStuckSearchMatchesForOneAndEightThreads()
    {
        var random = new Random(21);
        var targets = Enumerable.Range(0, NpcTargetCount)
            .Select(i => TestTargets.Create(i, TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)), (i % 2 == 0 ? Boulder : Building).Base, TestTargets.Space))
            .ToList();
        var npcs = Enumerable.Range(0, PlacedNpcCount)
            .Select(i => TestNpcs.Place(Mod, i, NpcBases[random.Next(NpcBases.Length)], RandomGroundPoint(random), new Vector3(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();

        var sequential = FindStuckNpcs(targets, npcs, SequentialThreads);
        var parallel = FindStuckNpcs(targets, npcs, ParallelThreads);

        Assert.NotEmpty(sequential.Hits);
        Assert.Equal(sequential.Hits, parallel.Hits);
        Assert.Equal(sequential.Work, parallel.Work);
        Assert.Equal(sequential.Summary, parallel.Summary);
    }

    [Fact]
    public void ShapeZoneSearchMatchesForOneAndEightThreads()
    {
        var scene = CreateShapeZoneScene();

        var sequential = FindShapeZoneHits(scene, TargetWorkOrder.Of(scene.Targets), SequentialThreads);
        var parallel = FindShapeZoneHits(scene, TargetWorkOrder.Of(scene.Targets), ParallelThreads);

        Assert.NotEmpty(sequential.Hits);
        Assert.NotEqual(0, sequential.Work.Zone.NarrowTests);
        Assert.NotEqual(0, sequential.Work.Zone.BoxZoneTargets);
        Assert.NotEqual(0, sequential.Work.Npcs.PairsTested);
        Assert.Equal(sequential.Hits, parallel.Hits);
        Assert.Equal(sequential.Work, parallel.Work);
    }

    [Fact]
    public void ShapeZoneSearchMatchesForAReversedWorkOrder()
    {
        var scene = CreateShapeZoneScene();
        var order = TargetWorkOrder.Of(scene.Targets);

        var forward = FindShapeZoneHits(scene, order, ParallelThreads);
        var reversed = FindShapeZoneHits(scene, new WorkOrder([.. order.ItemsInOrder.Reverse()]), ParallelThreads);

        Assert.Equal(forward.Hits, reversed.Hits);
        Assert.Equal(forward.Work, reversed.Work);
    }

    [Fact]
    public void LeftBehindSelectionMatchesForOneAndEightThreads()
    {
        var random = new Random(33);
        var targets = new List<TargetObject>();
        foreach (var position in GridPositions(random))
        {
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), Table.Base, TestTargets.Space));
        }
        var tableCount = targets.Count;
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        for (var i = 0; i < MarkerCount; i++)
        {
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomGridPoint(random)), xMarker, TestTargets.Space));
        }
        var removed = Enumerable.Range(0, tableCount).Where(_ => random.NextDouble() < 0.5).ToHashSet();
        var rooms = Enumerable.Range(0, RoomCount)
            .Select(i => TestShapes.Placed(Mod, i, Boulder.Base, RandomGridPoint(random)))
            .ToList();

        var sequential = SelectLeftBehind(targets, rooms, removed, SequentialThreads);
        var parallel = SelectLeftBehind(targets, rooms, removed, ParallelThreads);

        Assert.NotEqual(0, sequential.RemovedCount);
        Assert.Contains(sequential.Evaluations, evaluation => evaluation.ContainingObject != null);
        Assert.Equal(DescribeEvaluations(sequential), DescribeEvaluations(parallel));
    }

    private static TestTouchCascade.Run RunTouchCascade(RestingObjectsScene scene, int threads) =>
        TestTouchCascade.Execute(scene.Targets, Shapes, scene.Protection, scene.Seeds, TouchDistance, threads, collectDiagnostics: true);

    private static TestSupportCascade.Run RunSupportCascade(RestingObjectsScene scene, int threads) =>
        TestSupportCascade.Execute(
            scene.Targets,
            Shapes,
            scene.Protection,
            scene.Seeds,
            TestScenes.CreateWithSupportOnlyObjects(scene.Targets, [], Shapes, threads).VisibleObjectsOfAnyPlugin(),
            TestGround.NoTerrain(),
            TouchDistance,
            threshold: 0.5f,
            threads);

    private static (List<TooCloseObject> Hits, TooCloseWork Work, string Summary) FindStuckNpcs(List<TargetObject> targets, List<OtherObject> npcs, int threads)
    {
        var replacements = Replacements.None(npcs.Count);
        var scene = TestScenes.Create(targets, npcs, Shapes, bodies: NewBodies(), threads: threads);
        var npcRule = CreateStuckNpcRule(targets, scene, replacements);
        var otherModObjects = scene.ObjectsThatCanCauseRemovals(replacements, NpcHandling.OnlyWhenStuckInObject);
        var result = TooCloseSearch.FindTooCloseTargets(
            targets, otherModObjects, Shapes, multiplier: 0f, npcRule, TargetWorkOrder.Of(targets), Options(threads));
        var summary = npcRule.StuckSearch!.GetSummary(result.Work.Npcs);
        return (result.Hits, result.Work, $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}");
    }

    private static TooCloseSearchResult FindShapeZoneHits(ShapeZoneScene scene, WorkOrder order, int threads)
    {
        var replacements = Replacements.None(scene.OtherModObjects.Count);
        var sceneIndex = TestScenes.Create(scene.Targets, scene.OtherModObjects, Shapes, bodies: NewBodies(), threads: threads);
        var npcRule = CreateStuckNpcRule(scene.Targets, sceneIndex, replacements);
        var otherModObjects = sceneIndex.ObjectsThatCanCauseRemovals(replacements, NpcHandling.OnlyWhenStuckInObject);
        return ShapeZoneSearch.Create(scene.Targets, sceneIndex.VisibleTargets, otherModObjects, Shapes, NewCache(), ShapeZoneMultiplier, npcRule)
            .FindTooCloseTargets(order, Options(threads));
    }

    private static NpcTooCloseRule CreateStuckNpcRule(
        IReadOnlyList<TargetObject> targets, ObjectCaches scene, Replacements replacements) =>
        NpcTooCloseRule.Create(
            NpcHandling.OnlyWhenStuckInObject,
            () => NpcStuckSearch.Create(targets, scene.VisibleTargets, scene.NpcsThatCanSpawn(replacements), Shapes, NewCache()));

    /// <summary>
    /// Targets with and without meshes, every few of them invisible, among other mods' objects with
    /// meshes and placed NPCs.
    /// </summary>
    private static ShapeZoneScene CreateShapeZoneScene()
    {
        var random = new Random(45);
        BaseKey[] targetBases = [Table.Base, Boulder.Base, Building.Base, TestVisibility.TaggedBase(ObjectVisibility.Visible)];
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = Enumerable.Range(0, ShapeZoneTargetCount)
            .Select(i => TestTargets.Create(
                i,
                TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)),
                i % InvisibleEvery == 0 ? xMarker : targetBases[i % targetBases.Length],
                TestTargets.Space))
            .ToList();
        BaseKey[] otherModBases = [Item.Base, Table.Base, Boulder.Base];
        var otherModObjects = Enumerable.Range(0, ShapeZoneOtherModObjectCount)
            .Select(i => i % ObjectOtherModEvery == 0
                ? TestShapes.Placed(Mod, i, otherModBases[random.Next(otherModBases.Length)], RandomGroundPoint(random), TestMeshes.RandomAngle(random))
                : TestNpcs.Place(Mod, i, NpcBases[random.Next(NpcBases.Length)], RandomGroundPoint(random), new Vector3(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();
        return new ShapeZoneScene(targets, otherModObjects);
    }

    private static LeftBehindResult SelectLeftBehind(
        List<TargetObject> targets, List<OtherObject> rooms, HashSet<int> removed, int threads) =>
        TestLeftBehind.CreateRule(targets, Shapes, rooms, CreateLeftBehindConfig())
            .SelectRemovals(removed, Options(threads));

    /// <summary>A grid of tables close enough that some touch, items (some stacked) on most of them, random links and seeds.</summary>
    private static RestingObjectsScene CreateRestingObjectsScene()
    {
        var random = new Random(7);
        var targets = new List<TargetObject>();
        var tables = new List<int>();
        foreach (var position in GridPositions(random))
        {
            tables.Add(targets.Count);
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), Table.Base, TestTargets.Space));
            if (random.NextDouble() < 0.3) continue;
            var itemPosition = position + new Vector3(RandomOffset(random, 15), RandomOffset(random, 15), 10);
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(itemPosition), Item.Base, TestTargets.Space));
            if (random.NextDouble() < 0.3) targets.Add(TestTargets.Create(targets.Count, TestTargets.At(itemPosition + new Vector3(0, 0, 6)), Item.Base, TestTargets.Space));
        }

        var links = Enumerable.Range(0, LinkCount)
            .Select(_ => TestTargets.Link(random.Next(targets.Count), random.Next(targets.Count)))
            .ToList();
        var groups = LinkedGroups.Build(targets.Count, links);
        var seeds = tables.OrderBy(_ => random.Next()).Take(SeedCount)
            .SelectMany(groups.MembersOf)
            .Distinct()
            .Order()
            .ToList();
        var kept = Enumerable.Range(0, targets.Count).Where(index => !seeds.Contains(index)).OrderBy(_ => random.Next()).Take(KeptCount);
        var references = TestTargets.References(
            targets.Count,
            kept.ToDictionary(index => index, _ => TestKeepReasons.Quest));
        return new RestingObjectsScene(targets, seeds, ObjectsToKeep.Build(targets, links, references));
    }

    private static IEnumerable<Vector3> GridPositions(Random random)
    {
        for (var x = 0; x < GridSize; x++)
        {
            for (var y = 0; y < GridSize; y++)
            {
                yield return new Vector3(x * GridSpacing + RandomOffset(random, GridJitter), y * GridSpacing + RandomOffset(random, GridJitter), 0);
            }
        }
    }

    private static Vector3 RandomGridPoint(Random random) =>
        new((float)random.NextDouble() * GridSize * GridSpacing, (float)random.NextDouble() * GridSize * GridSpacing, 5);

    private static Vector3 RandomGroundPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 0);

    private static float RandomOffset(Random random, float extent) => (float)((random.NextDouble() * 2 - 1) * extent);

    private static string DescribeStats(TouchChainStatistics stats) =>
        $"{stats.Components} {stats.ComponentsWithRemovals} {stats.LargestComponent} {stats.Levels} {stats.MaxDepth} "
        + $"{stats.Pairs.PairsTested} {stats.Pairs.TouchingPairs} {stats.Pairs.TrianglePairsTested}";

    /// <summary>Each round with what it removed and kept, one line per round, so a failure shows where two runs part.</summary>
    private static List<string> DescribeRounds(IRemovalDecisions decisions) =>
    [
        .. decisions.Rounds.Select(round =>
            $"{round}: removed {string.Join(" ", decisions.RemovedIn(round).Select(target => target.Index))}; kept {string.Join(" ", decisions.KeptIn(round).Select(target => target.Index))}"),
    ];

    /// <summary>Everything but the edges' distances.</summary>
    private static string DescribeComponents(TouchChainEdges explanation) =>
        string.Join(
            "; ",
            string.Join(' ', explanation.Components.ComponentOf),
            string.Join(' ', explanation.Components.ParentOf),
            string.Join(' ', explanation.Components.DepthOf),
            string.Join(" | ", explanation.Components.Members.Select(members => string.Join(' ', members))),
            string.Join(' ', explanation.Edges.Select(edge => $"{edge.ComponentId}:{edge.Pair.First}-{edge.Pair.Second}")));

    private static List<string> DescribeEvaluations(RestingObjectsResult alsoRemove) =>
        AnchoringRows.Join(alsoRemove)
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Iteration} {evaluation.Removed} {evaluation.RemovedAsLinked} {evaluation.Held} {evaluation.RemovedShare}")
            .ToList();

    private static List<string> DescribeEvaluations(LeftBehindResult leftBehind) =>
        leftBehind.Evaluations
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Decision} {evaluation.ContainingObject?.Key} {evaluation.Radius} {SectorAreasText.Describe(evaluation.Surroundings)}")
            .ToList();

    private static LeftBehindOptions CreateLeftBehindConfig() => new(
        LookAround: 100,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        NeverRemove: new HashSet<InvisibleObjectKind>(),
        Preset: ProtectedInvisibleObjectsPreset.None);

    private static TriangleStore NewCache() => new(Shapes.ReadTriangles);

    private static NpcBodyCache NewBodies() =>
        TestNpcBodies.Create(Records.ToImmutableLinkCache(), Shapes);

    private static Execution Options(int threads) => new(threads);

    /// <summary>Non-playable race without a skin, so every NPC is sized by its Object Bounds.</summary>
    private static SkyrimMod CreateRecords()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Races.Add(TestNpcs.CreateRace(Race, skin: null, playable: false, armorRace: null));
        mod.Npcs.Add(WithBounds(TestNpcs.CreateNpc(SmallNpc, Race, female: false, template: null), 15, 60));
        mod.Npcs.Add(WithBounds(TestNpcs.CreateNpc(LargeNpc, Race, female: false, template: null), 40, 140));
        mod.Npcs.Add(TestNpcs.CreateNpc(LeveledTemplateNpc, Race, female: false, template: Leveled));
        mod.LeveledNpcs.Add(TestNpcs.CreateLeveledList(Leveled, SmallNpc, LargeNpc));
        return mod;
    }

    private static Mutagen.Bethesda.Skyrim.Npc WithBounds(Mutagen.Bethesda.Skyrim.Npc npc, short halfWidth, short height)
    {
        npc.ObjectBounds = new ObjectBounds { First = new P3Int16((short)-halfWidth, (short)-halfWidth, 0), Second = new P3Int16(halfWidth, halfWidth, height) };
        return npc;
    }
}
