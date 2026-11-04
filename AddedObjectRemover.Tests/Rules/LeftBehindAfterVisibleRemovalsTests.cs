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
    private static readonly TestStatic Building = new(new FormKey(OtherMod, 0x802), @"test\building.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "LeftBehindAfterVisibleRemovalsData", Block, Building));
    private static readonly BaseKey Light = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.Lights));

    private static readonly InvisibleLeftBehindSettings Settings = new(
        new LeftBehindSurroundings(LookAround: 1024, MinOccupiedDirections: 4, DirectionClearedPercent: 50, ClearedDirectionsPercent: 60));

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

    /// <summary>"Objects left behind" keeps an object with fewer than its minimum of the cleaned mod's objects around; this step has no such minimum.</summary>
    [Fact]
    public void ALightInsideOneRemovedBuildingOfTheCleanedModIsStillRemoved()
    {
        ImmutableArray<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(Vector3.Zero), Light, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(Vector3.Zero), Building.Base, TestTargets.Space),
        ];

        var light = Assert.Single(Find(targets, StartDecisions(targets).Apply(RoundKind.VisibleLeftBehind, [Removed(1)])).Evaluations);

        Assert.Equal((1, LeftBehindOutcome.RemovedSurroundingsRemoved), (light.Surroundings.Objects, light.Decision));
    }

    private static ProposedRemoval Removed(int index) => new(new TargetId(index), new VisibleObjectSurroundingsRemoved());

    private static RemovalDecisions StartDecisions() => StartDecisions(Targets);

    private static RemovalDecisions StartDecisions(ImmutableArray<TargetObject> targets) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length);

    private static InvisibleLeftBehindResult Find(IRunDecisions decisions) => Find(Targets, decisions);

    private static InvisibleLeftBehindResult Find(ImmutableArray<TargetObject> targets, IRunDecisions decisions)
    {
        var scene = TestScenes.Create(targets, [], Shapes, supportOnlyObjects: []);
        return InvisibleLeftBehindObjects.Find(
            new(
                targets,
                scene.ObjectsThatCanCauseRemovals(Replacements.None(0), NpcHandling.OnlyWhenStuckInsideTheObject),
                OtherModsObjectsResult.SwitchedOff,
                decisions,
                new TestRunCaches(Shapes, bases: TestBaseFacts.Unresolved, visibleTargets: scene.IndexVisibleTargets),
                TestLeftBehind.KindsAlwaysKept(Shapes),
                LeftBehindRules.CreateInvisibleObjectReach(TestBaseFacts.Unresolved),
                LeftBehindRules.CreateSurroundingsJudging(),
                LeftBehindRules.CreateLeftBehindRoundJoin(),
                TargetWorkOrder.Of(targets),
                new Execution(Environment.ProcessorCount),
                Settings)).Result;
    }
}
