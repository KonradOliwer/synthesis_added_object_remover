using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>
/// The follow-up loops, the NPC-stuck search and the leftover selection give the same results with
/// one and with eight threads, on synthetic scenes large enough to span many parallel batches.
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
    private static readonly BaseObjectShapeProvider Shapes = TestShapes.Create(Records, "StepDeterminismData", Table, Item, Boulder, Building);

    private sealed record FollowUpScene(List<TargetObject> Targets, List<int> Seeds, Protection Protection);

    [Fact]
    public void EverythingTouchingMatchesForOneAndEightThreads()
    {
        var scene = CreateFollowUpScene();

        var sequential = RunTouchCascade(scene, SequentialThreads);
        var parallel = RunTouchCascade(scene, ParallelThreads);

        Assert.NotEmpty(sequential.Clusters.Removals);
        Assert.Contains(sequential.Clusters.Removals, removal => removal is LinkedRemoval);
        Assert.Equal(DescribeRounds(sequential.Ledger), DescribeRounds(parallel.Ledger));
        Assert.Equal(sequential.Ledger.All().ToList(), parallel.Ledger.All().ToList());
        Assert.Equal(sequential.Clusters.Removals, parallel.Clusters.Removals);
        Assert.Equal(sequential.Clusters.Kept, parallel.Clusters.Kept);
        Assert.Equal(DescribeStats(sequential.Clusters.Stats), DescribeStats(parallel.Clusters.Stats));
        Assert.Equal(DescribeDiagnostics(sequential.Clusters.Diagnostics!), DescribeDiagnostics(parallel.Clusters.Diagnostics!));
    }

    [Fact]
    public void AnchoringMatchesForOneAndEightThreads()
    {
        var scene = CreateFollowUpScene();

        var sequential = RunAnchoring(scene, SequentialThreads);
        var parallel = RunAnchoring(scene, ParallelThreads);

        Assert.True(sequential.Stats.Iterations > 1);
        Assert.Equal(sequential.Removals, parallel.Removals);
        Assert.Equal(sequential.Kept, parallel.Kept);
        Assert.Equal(DescribeEvaluations(sequential), DescribeEvaluations(parallel));
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
        Assert.Equal(sequential.Summary, parallel.Summary);
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

        var sequential = SelectLeftovers(targets, visibility, rooms, removed, SequentialThreads);
        var parallel = SelectLeftovers(targets, visibility, rooms, removed, ParallelThreads);

        Assert.NotEqual(0, sequential.RemovedCount);
        Assert.Contains(sequential.Evaluations, evaluation => evaluation.ContainingObject != null);
        Assert.Equal(DescribeEvaluations(sequential), DescribeEvaluations(parallel));
    }

    private static TestTouchCascade.Run RunTouchCascade(FollowUpScene scene, int threads) =>
        TestTouchCascade.Execute(scene.Targets, Shapes, scene.Protection, scene.Seeds, TouchDistance, threads, collectDiagnostics: true);

    private static AnchoringResult RunAnchoring(FollowUpScene scene, int threads) =>
        AnchoringRemover.Run(
            scene.Targets,
            AllVisible(scene.Targets),
            scene.Seeds,
            keptTooClose: [],
            new SupporterIndex(new Dictionary<FormKey, List<OtherObject>>(), Shapes, Options(threads)),
            new TerrainHeights(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<FormKey, FormKey>()),
            Shapes,
            NewCache(),
            scene.Protection,
            TouchDistance,
            threshold: 0.5f,
            Options(threads));

    private static (List<TooCloseHit> Hits, string Summary) FindStuckNpcs(List<TargetObject> targets, List<OtherObject> npcs, int threads)
    {
        var options = Options(threads);
        var visibility = AllVisible(targets);
        var indexes = new Dictionary<FormKey, OtherObjectIndex> { [TestTargets.Space] = OtherObjectIndex.CreateUncounted(npcs, Shapes, options) };
        var replacements = Replacements.None(npcs.Count);
        var bodies = new NpcBodyCache(new NpcBodyResolver(Records.ToImmutableLinkCache(), Shapes, new SkinnedBodyMeasurer(Shapes.ReadGeometry)));
        var npcRule = NpcClashRule.Create(
            NpcHandling.OnlyWhenStuckInObject,
            () => NpcStuckSearch.Create(targets, visibility, indexes, replacements, bodies, Shapes, NewCache(), options));
        var hits = TooCloseSearch.FindTooCloseTargets(targets, visibility, indexes, replacements, Shapes, multiplier: 0f, npcRule, options);
        var summary = npcRule.StuckSearch!.GetSummary();
        return (hits, $"{summary.Sizes} {summary.PairsTested} {summary.CoreTests} {summary.Conflicts} {summary.PointFallbacks.Count}");
    }

    private static LeftoverResult SelectLeftovers(
        List<TargetObject> targets, List<ObjectVisibility> visibility, List<OtherObject> rooms, HashSet<int> removed, int threads) =>
        TestLeftovers.CreateSelector(targets, visibility, Shapes, rooms, CreateLeftoverConfig())
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

    /// <summary>Everything but the edges' distances and the timings.</summary>
    /// <summary>Each round with what it removed and held, one line per round, so a failure shows where two runs part.</summary>
    private static List<string> DescribeRounds(Ledger ledger) =>
    [
        .. ledger.Rounds.Select(round =>
            $"{round}: removed {string.Join(" ", ledger.RemovedIn(round).Select(target => target.Index))}; held {string.Join(" ", ledger.HeldIn(round).Select(target => target.Index))}"),
    ];

    private static string DescribeDiagnostics(TouchDiagnosticsData diagnostics) =>
        string.Join(
            "; ",
            string.Join(' ', diagnostics.ComponentId),
            string.Join(' ', diagnostics.ParentOf),
            string.Join(' ', diagnostics.Depth),
            string.Join(" | ", diagnostics.ComponentMembers.Select(members => string.Join(' ', members))),
            string.Join(' ', diagnostics.Edges.Select(edge => $"{edge.ComponentId}:{edge.Pair.First}-{edge.Pair.Second}")));

    private static List<string> DescribeEvaluations(AnchoringResult anchoring) =>
        anchoring.Evaluations
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Iteration} {evaluation.Removed} {evaluation.RemovedAsLinked} {evaluation.RemovedShare}")
            .ToList();

    private static List<string> DescribeEvaluations(LeftoverResult leftovers) =>
        leftovers.Evaluations
            .Select(evaluation => $"{evaluation.TargetIndex} {evaluation.Decision} {evaluation.ContainingObject?.FormKey} {evaluation.Radius} {evaluation.Surroundings.Describe()}")
            .ToList();

    private static LeftoverConfig CreateLeftoverConfig() => new(
        Enabled: true,
        SearchRadius: 100,
        DirectionThresholdPercent: 50,
        RemovedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        ProtectedPreset: ProtectedInvisibleObjectsPreset.None,
        ProtectedKinds: new HashSet<InvisibleObjectKind>(),
        MovesKeptMarkers: false);

    private static ObjectVisibility[] AllVisible(IReadOnlyList<TargetObject> targets) =>
        Enumerable.Repeat(ObjectVisibility.Visible, targets.Count).ToArray();

    private static TriangleTreeCache NewCache() => new(Shapes.ReadGeometry);

    private static ParallelOptions Options(int threads) => new() { MaxDegreeOfParallelism = threads };

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
