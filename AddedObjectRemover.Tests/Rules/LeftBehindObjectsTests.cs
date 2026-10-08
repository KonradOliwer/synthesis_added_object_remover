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

    private static readonly LeftBehindOptions Options = new(
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
        var expected = TestLeftBehind.CreateRule(Targets, Shapes, Others, Options)
            .SelectRemovals(new HashSet<int>(), new Execution(Environment.ProcessorCount));

        var actual = LeftBehindObjects.Evaluate(CreateInput(StartDecisions()), Options);

        Assert.Equal(Summarize(expected), Summarize(actual));
    }

    [Fact]
    public void CandidatesAreTheKeptEvaluationsInsideAnotherObject()
    {
        var evaluated = LeftBehindObjects.Evaluate(CreateInput(StartDecisions()), Options);
        Assert.Equal(0, Assert.Single(evaluated.Evaluations, evaluation => evaluation.ContainingObject != null).TargetIndex);

        var removedDecisions = StartDecisions().Apply(RoundKind.LeftBehind, LeftBehindRound.Proposals(evaluated));

        Assert.Equal(0, Assert.Single(MarkerMovePlanner.Candidates(evaluated, StartDecisions())).TargetIndex);
        Assert.Empty(MarkerMovePlanner.Candidates(evaluated, removedDecisions));
    }

    [Fact]
    public void PlanMovesAKeptMarkerToTheNearestFreeSpotWithinTheMaxDistance()
    {
        var evaluated = LeftBehindObjects.Evaluate(CreateInput(StartDecisions()), Options with { NeverRemove = new HashSet<InvisibleObjectKind> { InvisibleObjectKind.XMarkers } });
        var decisions = StartDecisions();
        var scene = CreateScene();
        var navmeshes = TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>> { [TestTargets.Space] = [new CellNavmesh((0, 0), SquareNavmesh(1000))] });

        MarkerMoves Plan(float maxDistance) => MarkerMovePlanner.Plan(
            new MarkerMoveInput(
                Targets,
                MarkerMovePlanner.Candidates(evaluated, decisions),
                new MarkerSurroundings(scene.VisibleObjectsOfAnyPlugin(),scene.VisibleTargets, decisions),
                new InteriorOnlyPlugin(),
                navmeshes,
                TestGround.NoTerrain(),
                Shapes,
                new Execution(Environment.ProcessorCount)),
            new MarkerMoveSettings(maxDistance));

        var far = Plan(MarkerMoveLimits.MaxDistance);
        Assert.Empty(far.LeftInPlace);
        var moved = Assert.Single(far.Moved);
        Assert.Equal(0, moved.Evaluation.TargetIndex);
        Assert.Equal(RelocationSurface.Navmesh, moved.Surface);

        var near = Plan(1);
        Assert.Empty(near.Moved);
        Assert.Single(near.LeftInPlace);
    }

    private static IEnumerable<(int Target, InvisibleObjectKind Kind, float Radius, OtherId? Host, LeftBehindOutcome Decision, int Occupied, int Removed)> Summarize(LeftBehindResult result) =>
        result.Evaluations.Select(e => (e.TargetIndex, e.Kind, e.Radius, e.ContainingObject?.Id, e.Decision, e.Surroundings.OccupiedCount, e.Surroundings.RemovedCount));

    private static RemovalDecisions StartDecisions() => RemovalDecisions.Start(ObjectsToKeep.Build(Targets, [], TestTargets.References(Targets.Length)), Targets.Length);

    private static ObjectCaches CreateScene() => TestScenes.Create(Targets, Others, Shapes, supportOnlyObjects: []);

    private static LeftBehindInput CreateInput(IRemovalDecisions decisions)
    {
        var scene = CreateScene();
        var order = TargetWorkOrder.Of(Targets);
        var execution = new Execution(Environment.ProcessorCount);
        return new LeftBehindInput(
            Targets,
            Hosts.Find(Targets, Shapes, scene.ObjectsThatCanCauseRemovals(Replacements.None(Others.Length), NpcHandling.OnlyWhenStuckInObject), order, execution),
            decisions,
            scene.VisibleTargets,
            new BaseFactsReader(new SkyrimMod(ModKey.FromNameAndExtension("Reach.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            Shapes,
            order,
            execution);
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
