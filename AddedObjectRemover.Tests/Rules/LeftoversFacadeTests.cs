using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The leftover and relocation facades give what the classes behind them give.</summary>
public class LeftoversFacadeTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly Vector3 InRoom = Vector3.Zero;

    private static readonly TestStatic Room = new(
        new FormKey(OtherMod, 0x801), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(OtherMod, "LeftoversFacadeData", Room);

    private static readonly LeftoverOptions Options = new(
        LookAround: 1024,
        DirectionClearedPercent: 50,
        ClearedDirectionsPercent: 60,
        OccupiedDirectionsPercent: 50,
        NeverRemove: new HashSet<InvisibleObjectKind>(),
        Preset: ProtectedInvisibleObjectsPreset.Custom);

    private static readonly ImmutableArray<TargetObject> Targets =
    [
        TestTargets.Create(0, TestTargets.At(InRoom)),
        TestTargets.Create(1, TestTargets.At(new Vector3(5000, 0, 0))),
    ];

    private static readonly TargetLooks Looks = new(
    [
        ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers),
        ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers),
    ]);

    private static readonly OtherObject[] Others = [TestShapes.Placed(OtherMod, 0, Room.Ref, InRoom)];

    [Fact]
    public void EvaluateGivesTheSelectorsEvaluations()
    {
        var expected = TestLeftovers.CreateSelector(Targets, Looks, Shapes, Others, Options)
            .SelectRemovals(new HashSet<int>(), new Execution(Environment.ProcessorCount));

        var actual = Leftovers.Evaluate(CreateInput(StartLedger()), Options);

        Assert.Equal(Summarize(expected), Summarize(actual));
    }

    [Fact]
    public void CandidatesAreTheKeptEvaluationsInsideAnotherObject()
    {
        var evaluated = Leftovers.Evaluate(CreateInput(StartLedger()), Options);
        Assert.Equal(0, Assert.Single(evaluated.Evaluations, evaluation => evaluation.ContainingObject != null).TargetIndex);

        var removedLedger = StartLedger().Apply(RoundKind.Leftover, evaluated.Proposals);

        Assert.Equal(0, Assert.Single(Relocations.Candidates(evaluated, StartLedger())).TargetIndex);
        Assert.Empty(Relocations.Candidates(evaluated, removedLedger));
    }

    [Fact]
    public void PlanMovesAKeptMarkerToTheNearestFreeSpotWithinTheMaxDistance()
    {
        var evaluated = Leftovers.Evaluate(CreateInput(StartLedger()), Options with { NeverRemove = new HashSet<InvisibleObjectKind> { InvisibleObjectKind.XMarkers } });
        var ledger = StartLedger();
        var scene = CreateScene();
        var navmeshes = new Dictionary<FormKey, List<CellNavmesh>> { [TestTargets.Space] = [new CellNavmesh((0, 0), SquareNavmesh(1000))] };

        RelocationResult Plan(float maxDistance) => Relocations.Plan(
            new RelocationInput(
                Targets,
                Relocations.Candidates(evaluated, ledger),
                new Obstacles(scene.Solids(), scene.VisibleTargets(Looks), ledger),
                CreateHandles(),
                navmeshes,
                new TerrainHeights(new Dictionary<ExteriorCell, ILandscapeGetter>(), new Dictionary<FormKey, FormKey>()),
                Shapes,
                new Execution(Environment.ProcessorCount)),
            new RelocationOptions(maxDistance));

        var far = Plan(KeptObjectRelocator.MaxMoveDistance);
        Assert.Empty(far.LeftInPlace);
        var moved = Assert.Single(far.Moved);
        Assert.Equal(0, moved.Evaluation.TargetIndex);
        Assert.Equal(RelocationSurface.Navmesh, moved.Surface);

        var near = Plan(1);
        Assert.Empty(near.Moved);
        Assert.Single(near.LeftInPlace);
    }

    private static IEnumerable<(int Target, InvisibleObjectKind Kind, float Radius, OtherId? Host, LeftoverDecision Decision, int Occupied, int Removed)> Summarize(LeftoverResult result) =>
        result.Evaluations.Select(e => (e.TargetIndex, e.Kind, e.Radius, e.ContainingObject?.Id, e.Decision, e.Surroundings.OccupiedCount, e.Surroundings.RemovedCount));

    private static Ledger StartLedger() => Ledger.Start(Protection.Build(Targets, [], TestTargets.References(Targets.Length)), Targets.Length);

    private static Scene CreateScene() => TestScenes.Create(Targets, Others, Shapes, backdrop: []);

    private static LeftoverInput CreateInput(Ledger ledger)
    {
        var scene = CreateScene();
        var order = WorkOrder.Of(Targets);
        var execution = new Execution(Environment.ProcessorCount);
        return new LeftoverInput(
            Targets,
            Looks,
            Hosts.Find(Targets, Looks, scene.ActiveRivals(Replacements.None(Others.Length), NpcHandling.OnlyWhenStuckInObject), order, execution),
            ledger,
            scene.VisibleTargets(Looks),
            new BaseFactsReader(new SkyrimMod(ModKey.FromNameAndExtension("Reach.esp"), SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            order,
            execution);
    }

    /// <summary>Every target stands in the interior cell that is its own space, so it may move anywhere.</summary>
    private static RecordHandles CreateHandles()
    {
        var mod = new SkyrimMod(TestTargets.TargetMod, SkyrimRelease.SkyrimSE);
        var cell = new Cell(TestTargets.Space, SkyrimRelease.SkyrimSE);
        mod.Cells.Add(new CellBlock { SubBlocks = { new CellSubBlock { Cells = { cell } } } });
        var context = mod.ToImmutableLinkCache().ResolveContext<ICell, ICellGetter>(TestTargets.Space);
        var location = new TargetLocation(context, InPersistentList: false);
        var record = new PlacedObject(TestTargets.Key(0), SkyrimRelease.SkyrimSE);
        return new RecordHandles([.. Targets.Select(_ => (IPlacedGetter)record)], [.. Targets.Select(_ => location)]);
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
