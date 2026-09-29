using System.Numerics;
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
    private const int ShapeZoneRivalCount = 120;
    private const int InvisibleEvery = 5;
    private const int ObjectRivalEvery = 4;
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
    private static readonly ShapeCatalog Shapes = TestShapes.Create(Records, "StepDeterminismData", Table, Item, Boulder, Building);

    private sealed record FollowUpScene(List<TargetObject> Targets, List<int> Seeds, Protection Protection);

    private sealed record ShapeZoneScene(List<TargetObject> Targets, TargetLooks Looks, List<OtherObject> Rivals);

    [Fact]
    public void EverythingTouchingMatchesForOneAndEightThreads()
    {
        var scene = CreateFollowUpScene();

        var sequential = RunTouchCascade(scene, SequentialThreads);
        var parallel = RunTouchCascade(scene, ParallelThreads);

        Assert.NotEqual(0, sequential.Result.CountRemovedByRule());
        Assert.Contains(
            sequential.FollowUpRounds.SelectMany(round => sequential.Ledger.RemovedIn(round)),
            target => sequential.Ledger.Of(target)!.Cause is Cause.Linked);
        Assert.Equal(DescribeRounds(sequential.Ledger), DescribeRounds(parallel.Ledger));
        Assert.Equal(sequential.Ledger.All().ToList(), parallel.Ledger.All().ToList());
        Assert.Equal(sequential.Result.Work, parallel.Result.Work);
        Assert.Equal(DescribeStats(sequential.Components.Stats), DescribeStats(parallel.Components.Stats));
        Assert.Equal(DescribeComponents(sequential.Explanation!), DescribeComponents(parallel.Explanation!));
    }

    [Fact]
    public void AnchoringMatchesForOneAndEightThreads()
    {
        var scene = CreateFollowUpScene();

        var sequential = RunSupportCascade(scene, SequentialThreads);
        var parallel = RunSupportCascade(scene, ParallelThreads);

        Assert.True(sequential.Result.Work.Rounds > 1);
        Assert.Equal(DescribeRounds(sequential.Ledger), DescribeRounds(parallel.Ledger));
        Assert.Equal(sequential.Ledger.All().ToList(), parallel.Ledger.All().ToList());
        Assert.Equal(DescribeEvaluations(sequential.Result), DescribeEvaluations(parallel.Result));
        Assert.Equal(sequential.Result.Work, parallel.Result.Work);
    }

    [Fact]
    public void NpcStuckSearchMatchesForOneAndEightThreads()
    {
        var random = new Random(21);
        var targets = Enumerable.Range(0, NpcTargetCount)
            .Select(i => TestTargets.Create(i, TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)), (i % 2 == 0 ? Boulder : Building).Ref, TestTargets.Space))
            .ToList();
        var npcs = Enumerable.Range(0, PlacedNpcCount)
            .Select(i => TestNpcs.Place(Mod, i, NpcBases[random.Next(NpcBases.Length)], RandomGroundPoint(random), new P3Float(0, 0, TestMeshes.RandomAngle(random))))
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

        var sequential = FindShapeZoneHits(scene, WorkOrder.Of(scene.Targets), SequentialThreads);
        var parallel = FindShapeZoneHits(scene, WorkOrder.Of(scene.Targets), ParallelThreads);

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
        var order = WorkOrder.Of(scene.Targets);

        var forward = FindShapeZoneHits(scene, order, ParallelThreads);
        var reversed = FindShapeZoneHits(scene, new WorkOrder([.. order.TargetsBySpaceAndCell.Reverse()]), ParallelThreads);

        Assert.Equal(forward.Hits, reversed.Hits);
        Assert.Equal(forward.Work, reversed.Work);
    }

    [Fact]
    public void LeftoverSelectionMatchesForOneAndEightThreads()
    {
        var random = new Random(33);
        var targets = new List<TargetObject>();
        var visibility = new List<ObjectVisibility>();
        foreach (var position in GridPositions(random))
        {
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), Table.Ref, TestTargets.Space));
            visibility.Add(ObjectVisibility.Visible);
        }
        var tableCount = targets.Count;
        for (var i = 0; i < MarkerCount; i++)
        {
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomGridPoint(random)), baseRef: null, TestTargets.Space));
            visibility.Add(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        }
        var removed = Enumerable.Range(0, tableCount).Where(_ => random.NextDouble() < 0.5).ToHashSet();
        var rooms = Enumerable.Range(0, RoomCount)
            .Select(i => TestShapes.Placed(Mod, i, Boulder.Ref, RandomGridPoint(random)))
            .ToList();

        var sequential = SelectLeftovers(targets, new TargetLooks([.. visibility]), rooms, removed, SequentialThreads);
        var parallel = SelectLeftovers(targets, new TargetLooks([.. visibility]), rooms, removed, ParallelThreads);

        Assert.NotEqual(0, sequential.RemovedCount);
        Assert.Contains(sequential.Evaluations, evaluation => evaluation.ContainingObject != null);
        Assert.Equal(DescribeEvaluations(sequential), DescribeEvaluations(parallel));
    }

    private static TestTouchCascade.Run RunTouchCascade(FollowUpScene scene, int threads) =>
        TestTouchCascade.Execute(scene.Targets, Shapes, scene.Protection, scene.Seeds, TouchDistance, threads, collectDiagnostics: true);

    private static TestSupportCascade.Run RunSupportCascade(FollowUpScene scene, int threads) =>
        TestSupportCascade.Execute(
            scene.Targets,
            Shapes,
            scene.Protection,
            scene.Seeds,
            TestScenes.CreateWithBackdrop(scene.Targets, [], Shapes, threads).Solids(),
            new TerrainHeights(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<FormKey, FormKey>()),
            TouchDistance,
            threshold: 0.5f,
            threads);

    private static (List<TooCloseHit> Hits, ClashWork Work, string Summary) FindStuckNpcs(List<TargetObject> targets, List<OtherObject> npcs, int threads)
    {
        var visibility = AllVisible(targets);
        var replacements = Replacements.None(npcs.Count);
        var scene = TestScenes.Create(targets, npcs, Shapes, bodies: NewBodies(), threads: threads);
        var npcRule = CreateStuckNpcRule(targets, visibility, scene, replacements);
        var rivals = scene.ActiveRivals(replacements, NpcHandling.OnlyWhenStuckInObject);
        var result = TooCloseSearch.FindTooCloseTargets(
            targets, visibility, rivals, Shapes, multiplier: 0f, npcRule, WorkOrder.Of(targets), Options(threads));
        var summary = npcRule.StuckSearch!.GetSummary(result.Work.Npcs);
        return (result.Hits, result.Work, $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}");
    }

    private static ClashSearchResult FindShapeZoneHits(ShapeZoneScene scene, WorkOrder order, int threads)
    {
        var replacements = Replacements.None(scene.Rivals.Count);
        var sceneIndex = TestScenes.Create(scene.Targets, scene.Rivals, Shapes, bodies: NewBodies(), threads: threads);
        var npcRule = CreateStuckNpcRule(scene.Targets, scene.Looks, sceneIndex, replacements);
        var rivals = sceneIndex.ActiveRivals(replacements, NpcHandling.OnlyWhenStuckInObject);
        return ShapeZoneSearch.Create(scene.Targets, scene.Looks, rivals, Shapes, NewCache(), ShapeZoneMultiplier, npcRule)
            .FindTooCloseTargets(scene.Looks, order, Options(threads));
    }

    private static NpcClashRule CreateStuckNpcRule(
        IReadOnlyList<TargetObject> targets, TargetLooks looks, Scene scene, Replacements replacements) =>
        NpcClashRule.Create(
            NpcHandling.OnlyWhenStuckInObject,
            () => NpcStuckSearch.Create(targets, looks, scene.Npcs(replacements), Shapes, NewCache()));

    /// <summary>
    /// Targets with and without meshes, every few of them invisible, among other mods' objects with
    /// meshes and placed NPCs.
    /// </summary>
    private static ShapeZoneScene CreateShapeZoneScene()
    {
        var random = new Random(45);
        BaseRef?[] targetBases = [Table.Ref, Boulder.Ref, Building.Ref, null];
        var targets = Enumerable.Range(0, ShapeZoneTargetCount)
            .Select(i => TestTargets.Create(i, TestTargets.At(RandomGroundPoint(random), TestMeshes.RandomAngle(random)), targetBases[i % targetBases.Length], TestTargets.Space))
            .ToList();
        var visibility = Enumerable.Range(0, targets.Count)
            .Select(i => i % InvisibleEvery == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers) : ObjectVisibility.Visible)
            .ToList();
        BaseRef[] rivalBases = [Item.Ref, Table.Ref, Boulder.Ref];
        var rivals = Enumerable.Range(0, ShapeZoneRivalCount)
            .Select(i => i % ObjectRivalEvery == 0
                ? TestShapes.Placed(Mod, i, rivalBases[random.Next(rivalBases.Length)], RandomGroundPoint(random), TestMeshes.RandomAngle(random))
                : TestNpcs.Place(Mod, i, NpcBases[random.Next(NpcBases.Length)], RandomGroundPoint(random), new P3Float(0, 0, TestMeshes.RandomAngle(random))))
            .ToList();
        return new ShapeZoneScene(targets, new TargetLooks([.. visibility]), rivals);
    }

    private static LeftoverResult SelectLeftovers(
        List<TargetObject> targets, TargetLooks looks, List<OtherObject> rooms, HashSet<int> removed, int threads) =>
        TestLeftovers.CreateSelector(targets, looks, Shapes, rooms, CreateLeftoverConfig())
            .SelectRemovals(removed, Options(threads));

    /// <summary>A grid of tables close enough that some touch, items (some stacked) on most of them, random links and seeds.</summary>
    private static FollowUpScene CreateFollowUpScene()
    {
        var random = new Random(7);
        var targets = new List<TargetObject>();
        var tables = new List<int>();
        foreach (var position in GridPositions(random))
        {
            tables.Add(targets.Count);
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), Table.Ref, TestTargets.Space));
            if (random.NextDouble() < 0.3) continue;
            var itemPosition = position + new Vector3(RandomOffset(random, 15), RandomOffset(random, 15), 10);
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(itemPosition), Item.Ref, TestTargets.Space));
            if (random.NextDouble() < 0.3) targets.Add(TestTargets.Create(targets.Count, TestTargets.At(itemPosition + new Vector3(0, 0, 6)), Item.Ref, TestTargets.Space));
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
            kept.ToDictionary(index => index, _ => new KeepReason(KeepKind.NonPlacedReference, "QUST record", "linked from QUST")));
        return new FollowUpScene(targets, seeds, Protection.Build(targets, links, references));
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

    private static string DescribeStats(TouchStats stats) =>
        $"{stats.Components} {stats.ComponentsWithRemovals} {stats.LargestComponent} {stats.Levels} {stats.MaxDepth} "
        + $"{stats.Pairs.PairsTested} {stats.Pairs.TouchingPairs} {stats.Pairs.TrianglePairsTested}";

    /// <summary>Each round with what it removed and held, one line per round, so a failure shows where two runs part.</summary>
    private static List<string> DescribeRounds(Ledger ledger) =>
    [
        .. ledger.Rounds.Select(round =>
            $"{round}: removed {string.Join(" ", ledger.RemovedIn(round).Select(target => target.Index))}; held {string.Join(" ", ledger.HeldIn(round).Select(target => target.Index))}"),
    ];

    /// <summary>Everything but the edges' distances.</summary>
    private static string DescribeComponents(TouchExplanation explanation) =>
        string.Join(
            "; ",
            string.Join(' ', explanation.Components.ComponentOf),
            string.Join(' ', explanation.Components.ParentOf),
            string.Join(' ', explanation.Components.DepthOf),
            string.Join(" | ", explanation.Components.Members.Select(members => string.Join(' ', members))),
            string.Join(' ', explanation.Edges.Select(edge => $"{edge.ComponentId}:{edge.Pair.First}-{edge.Pair.Second}")));

    private static List<string> DescribeEvaluations(FollowUpResult followUp) =>
        AnchoringRows.Join(followUp)
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Iteration} {evaluation.Removed} {evaluation.RemovedAsLinked} {evaluation.Held} {evaluation.RemovedShare}")
            .ToList();

    private static List<string> DescribeEvaluations(LeftoverResult leftovers) =>
        leftovers.Evaluations
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Decision} {evaluation.ContainingObject?.FormKey} {evaluation.Radius} {evaluation.Surroundings.Describe()}")
            .ToList();

    private static LeftoverOptions CreateLeftoverConfig() => new(
        LookAround: 100,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        NeverRemove: new HashSet<InvisibleObjectKind>(),
        Preset: ProtectedInvisibleObjectsPreset.None);

    private static TargetLooks AllVisible(IReadOnlyList<TargetObject> targets) => TestSeededLedger.AllVisible(targets.Count);

    private static TriangleStore NewCache() => new(Shapes.ReadGeometry);

    private static NpcBodyCache NewBodies() =>
        new(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, new SkinnedBodyMeasurer(Shapes.ReadGeometry)));

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
