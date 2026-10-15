using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The left-behind and marker-move facades give what the classes behind them give.</summary>
public class LeftBehindObjectsTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly Vector3 InRoom = Vector3.Zero;

    private static readonly TestStatic Room = new(
        new FormKey(OtherMod, 0x801), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "LeftBehindObjectsData", Room));

    private static readonly LeftBehindSettings Settings = new(
        LookAround: 1024,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        NeverRemove: new HashSet<InvisibleObjectKind>(),
        Preset: ProtectedInvisibleObjectsPreset.Custom);

    private static readonly BaseKey XMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    private static readonly ImmutableArray<TargetObject> Targets =
    [
        TestTargets.Create(0, TestTargets.At(InRoom), XMarker, TestTargets.Space),
        TestTargets.Create(1, TestTargets.At(new Vector3(5000, 0, 0)), XMarker, TestTargets.Space),
    ];

    private static readonly OtherObject[] Others = [TestShapes.Placed(OtherMod, 0, Room.Base, InRoom)];

    [Fact]
    public void EvaluateGivesTheSelectorsEvaluations()
    {
        var expected = TestLeftBehind.CreateRule(Targets, Shapes, Others, Settings)
            .SelectRemovals(new HashSet<int>(), new Execution(Environment.ProcessorCount));

        var actual = Find(StartDecisions(), Settings);

        Assert.Equal(StepOutcome.Performed, actual.Outcome);
        Assert.Equal(Summarize(expected), Summarize(actual));
    }

    [Fact]
    public void ASwitchedOffStepGivesNoEvaluationsAndNoProposals()
    {
        var result = Find(StartDecisions(), settings: null);

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void TheProposalsAreTheRemovedEvaluations()
    {
        var result = Find(StartDecisions(), Settings);

        var proposal = Assert.Single(result.Proposals);
        Assert.Equal(0, proposal.Target.Index);
        Assert.Equal(result.Evaluations.Where(evaluation => evaluation.IsRemoved).Select(evaluation => evaluation.TargetIndex), result.Proposals.Select(p => p.Target.Index));
    }

    [Fact]
    public void TheStepDecidesItsRoundOnACopyAndLeavesTheGivenDecisionsAsTheyWere()
    {
        var decisions = StartDecisions();

        var result = Find(decisions, Settings);

        Assert.NotEmpty(result.Proposals);
        Assert.Empty(decisions.Rounds);
        Assert.Empty(decisions.RemovedTargets());
    }

    [Fact]
    public void CandidatesAreTheKeptEvaluationsInsideAnotherObject()
    {
        var evaluated = Find(StartDecisions(), Settings);
        Assert.Equal(0, Assert.Single(evaluated.Evaluations, evaluation => evaluation.ContainingObject != null).TargetIndex);

        var removedDecisions = StartDecisions().Apply(RoundKind.LeftBehind, [.. evaluated.Proposals]);

        Assert.Equal(0, Assert.Single(MarkerMovePlanner.Candidates(evaluated, StartDecisions())).TargetIndex);
        Assert.Empty(MarkerMovePlanner.Candidates(evaluated, removedDecisions));
    }

    [Fact]
    public void PlanMovesAKeptMarkerToTheNearestFreeSpotWithinTheMaxDistance()
    {
        var far = Plan(MarkerMoveLimits.MaxDistance);
        Assert.Equal(StepOutcome.Performed, far.Outcome);
        Assert.Empty(far.LeftInPlace);
        Assert.Equal(0, Assert.Single(far.Moves).Target.Index);
        var detail = Assert.Single(far.Details);
        Assert.Equal(0, detail.Evaluation.TargetIndex);
        Assert.Equal(RelocationSurface.Navmesh, detail.Surface);

        var near = Plan(1);
        Assert.Empty(near.Moves);
        Assert.Empty(near.Details);
        Assert.Single(near.LeftInPlace);
    }

    [Fact]
    public void ASwitchedOffMarkerMoveStepMovesNothingAndNeverAsksForTheObjectsOfAnyPlugin()
    {
        var asked = false;

        var result = Plan(settings: null, () => { asked = true; return CreateScene().VisibleObjectsOfAnyPlugin(); });

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Moves);
        Assert.Empty(result.Details);
        Assert.Empty(result.LeftInPlace);
        Assert.False(asked);
    }

    [Fact]
    public void EveryMoveHasItsDetailInTheSameOrder()
    {
        var result = Plan(new MarkerMoveSettings(MarkerMoveLimits.MaxDistance));

        Assert.Equal(new TargetId(Assert.Single(result.Details).Evaluation.TargetIndex), Assert.Single(result.Moves).Target);
    }

    [Fact]
    public void AFailureInsideTheLeftBehindStepStopsItWithTheFailureAndNoProposals()
    {
        var scene = CreateScene();

        var result = LeftBehindObjects.Find(
            Targets,
            scene.ObjectsThatCanCauseRemovals(Replacements.None(Others.Length), NpcHandling.OnlyWhenStuckInObject),
            StartDecisions(),
            new FailingSurroundings(scene.VisibleTargets),
            new BaseFactsReader(new SkyrimMod(ModKey.FromNameAndExtension("Reach.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            Shapes,
            TargetWorkOrder.Of(Targets),
            new Execution(Environment.ProcessorCount),
            Settings);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Equal(new PartFailure(LeftBehindParts.LeftBehind, "InvalidOperationException: it broke", Subject: null), result.Failure);
        Assert.Empty(result.Evaluations);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void AFailureInsideTheMarkerMoveStepStopsItWithTheFailureAndNoMoves()
    {
        var result = Plan(new MarkerMoveSettings(MarkerMoveLimits.MaxDistance), () => throw new InvalidOperationException("it broke."));

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Equal(new PartFailure(MarkerMoveParts.MarkerMoves, "InvalidOperationException: it broke", Subject: null), result.Failure);
        Assert.Empty(result.Moves);
        Assert.Empty(result.Details);
        Assert.Empty(result.LeftInPlace);
    }

    [Fact]
    public void AStoppedLeftBehindStepMeansTheMarkerMoveStepIsNotPerformedAndAsksForNothing()
    {
        var asked = false;
        var scene = CreateScene();

        var result = MarkerMovePlanner.Plan(
            Targets,
            new LeftBehindResult(
                StepOutcome.StoppedByUnexpectedError, [], [], new PartFailure(LeftBehindParts.LeftBehind, "InvalidOperationException: it broke", Subject: null)),
            StartDecisions(),
            () =>
            {
                asked = true;
                return scene.VisibleObjectsOfAnyPlugin();
            },
            scene.VisibleTargets,
            new InteriorOnlyPlugin(),
            TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>>()),
            TestGround.NoTerrain(),
            Shapes,
            new Execution(Environment.ProcessorCount),
            new MarkerMoveSettings(MarkerMoveLimits.MaxDistance));

        Assert.Equal(StepOutcome.StoppedByEarlierFailure, result.Outcome);
        Assert.Null(result.Failure);
        Assert.Empty(result.Moves);
        Assert.Empty(result.Details);
        Assert.Empty(result.LeftInPlace);
        Assert.False(asked);
    }

    private static MarkerMoves Plan(float maxDistance) => Plan(new MarkerMoveSettings(maxDistance));

    private sealed class FailingSurroundings(IVisibleTargetObjects inner) : IVisibleTargetObjects
    {
        public IReadOnlyList<RecordKey> Spaces => inner.Spaces;

        public void Around(RecordKey space, Vector3 point, float radius, List<VisibleTargetNear> into) => throw new InvalidOperationException("it broke.");

        public bool AnyContains(RecordKey space, Vector3 point, Func<TargetId, bool> include, ObjectQueryScratch scratch) => inner.AnyContains(space, point, include, scratch);

        public IEnumerable<OrientedBox> BoxesNear(RecordKey space, Vector3 point, float radius, Func<TargetId, bool> include) =>
            inner.BoxesNear(space, point, radius, include);
    }

    private static MarkerMoves Plan(MarkerMoveSettings? settings, Func<IVisibleObjectsOfAnyPlugin>? objectsOfAnyPlugin = null)
    {
        var evaluated = Find(StartDecisions(), Settings with { NeverRemove = new HashSet<InvisibleObjectKind> { InvisibleObjectKind.XMarkers } });
        var scene = CreateScene();
        return MarkerMovePlanner.Plan(
            Targets,
            evaluated,
            StartDecisions(),
            objectsOfAnyPlugin ?? scene.VisibleObjectsOfAnyPlugin,
            scene.VisibleTargets,
            new InteriorOnlyPlugin(),
            TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>> { [TestTargets.Space] = [new CellNavmesh((0, 0), SquareNavmesh(1000))] }),
            TestGround.NoTerrain(),
            Shapes,
            new Execution(Environment.ProcessorCount),
            settings);
    }

    private static IEnumerable<(int Target, InvisibleObjectKind Kind, float Radius, OtherId? Host, LeftBehindOutcome Decision, int Occupied, int Removed)> Summarize(LeftBehindResult result) =>
        result.Evaluations.Select(e => (e.TargetIndex, e.Kind, e.Radius, e.ContainingObject?.Id, e.Decision, e.Surroundings.OccupiedCount, e.Surroundings.RemovedCount));

    private static RemovalDecisions StartDecisions() => RemovalDecisions.Start(ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Length)), Targets.Length);

    private static ObjectCaches CreateScene() => TestScenes.Create(Targets, Others, Shapes, supportOnlyObjects: []);

    private static LeftBehindResult Find(IRemovalDecisions decisions, LeftBehindSettings? settings)
    {
        var scene = CreateScene();
        return LeftBehindObjects.Find(
            Targets,
            scene.ObjectsThatCanCauseRemovals(Replacements.None(Others.Length), NpcHandling.OnlyWhenStuckInObject),
            decisions,
            scene.VisibleTargets,
            new BaseFactsReader(new SkyrimMod(ModKey.FromNameAndExtension("Reach.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            Shapes,
            TargetWorkOrder.Of(Targets),
            new Execution(Environment.ProcessorCount),
            settings);
    }

    /// <summary>Every target stands in an interior cell, so it may move anywhere and its home cell is never asked for.</summary>
    private sealed class InteriorOnlyPlugin : IPluginRecords
    {
        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

        public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }

    private static NavigationMeshData SquareNavmesh(float size) => new()
    {
        Vertices = new ExtendedList<P3Float> { new(0, 0, 0), new(size, 0, 0), new(size, size, 0), new(0, size, 0) },
        Triangles = new ExtendedList<NavmeshTriangle>
        {
            new() { Vertices = new P3Int16(0, 1, 3) },
            new() { Vertices = new P3Int16(2, 3, 1) },
        },
    };
}
