using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>The leftover facade gives the same evaluations and proposals whatever order the work is walked in.</summary>
public class LeftoverOrderDeterminismTests
{
    private const int Threads = 8;
    private const int TargetCount = 120;
    private const int RoomCount = 4;
    private const int TableEvery = 3;
    private const int RemovedTableEvery = 2;
    private const float SceneExtent = 400f;
    private const float RoomHalfSize = 60f;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("LeftoverOrder.esp");

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Room = new(
        new FormKey(Mod, 0x702), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RoomHalfSize), new Vector3(RoomHalfSize))));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(Mod, "LeftoverOrderData", Table, Room);

    private static readonly LeftoverOptions Options = new(
        LookAround: 100,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        NeverRemove: new HashSet<InvisibleObjectKind>(),
        Preset: ProtectedInvisibleObjectsPreset.None);

    [Fact]
    public void EvaluateMatchesForReversedAndShuffledWorkOrders()
    {
        var random = new Random(61);
        var targets = new List<TargetObject>();
        var visibility = new List<ObjectVisibility>();
        for (var i = 0; i < TargetCount; i++)
        {
            var isTable = i % TableEvery == 0;
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomPoint(random)), isTable ? Table.Ref : null, TestTargets.Space));
            visibility.Add(isTable ? ObjectVisibility.Visible : ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        }
        var rooms = Enumerable.Range(0, RoomCount).Select(i => TestShapes.Placed(Mod, i, Room.Ref, RandomPoint(random))).ToList();
        var looks = new TargetLooks([.. visibility]);
        var removedTables = Enumerable.Range(0, TargetCount).Where(i => i % TableEvery == 0 && i / TableEvery % RemovedTableEvery == 0).ToList();
        var scene = TestScenes.Create(targets, rooms, Shapes, backdrop: [], threads: Threads);
        var normal = WorkOrder.Of(targets);

        var expected = Evaluate(targets, looks, rooms, scene, removedTables, normal);

        Assert.Contains(expected.Evaluations, evaluation => evaluation.ContainingObject != null);
        Assert.Contains(expected.Evaluations, evaluation => evaluation.ContainingObject == null);
        foreach (var order in ShuffledWorkOrders.Variants(normal))
        {
            var actual = Evaluate(targets, looks, rooms, scene, removedTables, order);
            Assert.Equal(Describe(expected), Describe(actual));
            Assert.Equal(expected.Proposals.Select(proposal => proposal.Target.Index), actual.Proposals.Select(proposal => proposal.Target.Index));
        }
    }

    private static LeftoverResult Evaluate(
        List<TargetObject> targets, TargetLooks looks, List<OtherObject> rooms, Scene scene, List<int> removedTables, WorkOrder order)
    {
        var execution = new Execution(Threads);
        var immutableTargets = targets.ToImmutableArray();
        var protection = Protection.Build(immutableTargets, [], TestTargets.References(targets.Count));
        var ledger = TestSeededLedger.Seed(protection, targets.Count, removedTables);
        var rivals = scene.ActiveRivals(Replacements.None(rooms.Count), NpcHandling.OnlyWhenStuckInObject);
        var input = new LeftoverInput(
            immutableTargets,
            looks,
            Hosts.Find(immutableTargets, looks, rivals, order, execution),
            ledger,
            scene.VisibleTargets(looks),
            new BaseFactsReader(new SkyrimMod(ModKey.FromNameAndExtension("Facts.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            order,
            execution);
        return Leftovers.Evaluate(input, Options);
    }

    private static Vector3 RandomPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 5);

    private static List<string> Describe(LeftoverResult result) =>
        [.. result.Evaluations.Select(e =>
            $"{e.TargetIndex} {e.Kind} {e.Decision} {e.ContainingObject?.Id} {e.Radius} {e.Surroundings.OccupiedCount} {e.Surroundings.RemovedCount} {e.Surroundings.Describe()}")];
}
