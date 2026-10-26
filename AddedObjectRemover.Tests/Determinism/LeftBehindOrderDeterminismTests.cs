using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>The left-behind facade gives the same evaluations and proposals whatever order the work is walked in.</summary>
public class LeftBehindOrderDeterminismTests
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

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(Mod, "LeftoverOrderData", Table, Room));

    private static readonly LeftBehindSettings Settings = new(
        LookAround: 100,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        MinOccupiedDirections: 4,
        NeverRemove: new HashSet<InvisibleObjectKind>());

    [Fact]
    public void EvaluateMatchesForReversedAndShuffledWorkOrders()
    {
        var random = new Random(61);
        var targets = new List<TargetObject>();
        var xMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        for (var i = 0; i < TargetCount; i++)
        {
            var isTable = i % TableEvery == 0;
            targets.Add(TestTargets.Create(targets.Count, TestTargets.At(RandomPoint(random)), isTable ? Table.Base : xMarker, TestTargets.Space));
        }
        var rooms = Enumerable.Range(0, RoomCount).Select(i => TestShapes.Placed(Mod, i, Room.Base, RandomPoint(random))).ToList();
        var removedTables = Enumerable.Range(0, TargetCount).Where(i => i % TableEvery == 0 && i / TableEvery % RemovedTableEvery == 0).ToList();
        var scene = TestScenes.Create(targets, rooms, Shapes, supportOnlyObjects: [], threads: Threads);
        var normal = TargetWorkOrder.Of(targets);

        var expected = Evaluate(targets, rooms, scene, removedTables, normal);

        Assert.Contains(expected.Evaluations, evaluation => evaluation.ContainingObject != null);
        Assert.Contains(expected.Evaluations, evaluation => evaluation.ContainingObject == null);
        foreach (var order in ShuffledWorkOrders.Variants(normal))
        {
            var actual = Evaluate(targets, rooms, scene, removedTables, order);
            Assert.Equal(Describe(expected), Describe(actual));
            Assert.Equal(expected.Proposals.Select(proposal => proposal.Target.Index), actual.Proposals.Select(proposal => proposal.Target.Index));
        }
    }

    private static LeftBehindResult Evaluate(
        List<TargetObject> targets, List<OtherObject> rooms, ObjectCaches scene, List<int> removedTables, WorkOrder order)
    {
        var execution = new Execution(Threads);
        var immutableTargets = targets.ToImmutableArray();
        var protection = ObjectsToKeep.Build(immutableTargets, [], TestTargets.References(targets.Count), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.Seed(protection, targets.Count, removedTables);
        var otherModObjects = scene.ObjectsThatCanCauseRemovals(Replacements.None(rooms.Count), NpcHandling.OnlyWhenStuckInsideTheObject);
        return LeftBehindObjects.Find(
            immutableTargets,
            otherModObjects,
            new HashSet<OtherId>(),
            decisions,
            scene.VisibleTargets,
            TestBaseFacts.Reader(new SkyrimMod(ModKey.FromNameAndExtension("Facts.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            Shapes,
            order,
            execution,
            Settings);
    }

    private static Vector3 RandomPoint(Random random) =>
        new((float)random.NextDouble() * SceneExtent, (float)random.NextDouble() * SceneExtent, 5);

    private static List<string> Describe(LeftBehindResult result) =>
        [.. result.Evaluations.Select(e =>
            $"{e.TargetIndex} {e.Kind} {e.Decision} {e.ContainingObject?.Id} {e.Radius} {e.Surroundings.OccupiedCount} {e.Surroundings.RemovedCount} {SectorAreasText.Describe(e.Surroundings)}")];
}
