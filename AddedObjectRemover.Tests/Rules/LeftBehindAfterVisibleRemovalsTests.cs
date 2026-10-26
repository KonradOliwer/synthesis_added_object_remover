using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The invisible left-behind step counts the objects the visible left-behind round removed like any other removed object.</summary>
public class LeftBehindAfterVisibleRemovalsTests
{
    private const float RingDistance = 200f;

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly TestStatic Block = new(new FormKey(OtherMod, 0x801), @"test\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "LeftBehindAfterVisibleRemovalsData", Block));

    private static readonly LeftBehindSettings Settings = new(
        LookAround: 1024,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        MinOccupiedDirections: 4,
        NeverRemove: new HashSet<InvisibleObjectKind>());

    private static readonly BaseKey XMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    /// <summary>The marker at the origin and a visible object to its east, north, west and south.</summary>
    private static readonly ImmutableArray<TargetObject> Targets =
    [
        TestTargets.Create(0, TestTargets.At(Vector3.Zero), XMarker, TestTargets.Space),
        TestTargets.Create(1, TestTargets.At(new Vector3(RingDistance, 0, 0)), Block.Base, TestTargets.Space),
        TestTargets.Create(2, TestTargets.At(new Vector3(0, RingDistance, 0)), Block.Base, TestTargets.Space),
        TestTargets.Create(3, TestTargets.At(new Vector3(-RingDistance, 0, 0)), Block.Base, TestTargets.Space),
        TestTargets.Create(4, TestTargets.At(new Vector3(0, -RingDistance, 0)), Block.Base, TestTargets.Space),
    ];

    [Fact]
    public void TheVisibleObjectsAVisibleLeftBehindRoundRemovedClearTheMarkersSurroundings()
    {
        var decisions = StartDecisions().Apply(RoundKind.VisibleLeftBehind, [.. new[] { 1, 2, 3, 4 }.Select(Removed)]);

        var marker = Assert.Single(Find(decisions).Evaluations);

        Assert.Equal(4, marker.Surroundings.OccupiedCount);
        Assert.Equal(4, marker.Surroundings.RemovedCount);
        Assert.Equal(LeftBehindOutcome.RemovedSurroundingsRemoved, marker.Decision);
    }

    [Fact]
    public void WithoutTheirRemovalTheMarkersSurroundingsAreMostlyKept()
    {
        var marker = Assert.Single(Find(StartDecisions().Apply(RoundKind.VisibleLeftBehind, [Removed(1)])).Evaluations);

        Assert.Equal(1, marker.Surroundings.RemovedCount);
        Assert.Equal(LeftBehindOutcome.KeptSurroundingsMostlyKept, marker.Decision);
    }

    private static ProposedRemoval Removed(int index) => new(new TargetId(index), new RemovalReason.VisibleObjectSurroundingsRemoved());

    private static RemovalDecisions StartDecisions() =>
        RemovalDecisions.Start(ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Length), TestBaseFacts.Unresolved), Targets.Length);

    private static LeftBehindResult Find(IRemovalDecisions decisions)
    {
        var scene = TestScenes.Create(Targets, [], Shapes, supportOnlyObjects: []);
        return LeftBehindObjects.Find(
            Targets,
            scene.ObjectsThatCanCauseRemovals(Replacements.None(0), NpcHandling.OnlyWhenStuckInsideTheObject),
            new HashSet<OtherId>(),
            decisions,
            scene.VisibleTargets,
            TestBaseFacts.Unresolved,
            Shapes,
            TargetWorkOrder.Of(Targets),
            new Execution(Environment.ProcessorCount),
            Settings);
    }
}
