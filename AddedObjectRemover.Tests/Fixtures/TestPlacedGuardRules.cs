using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The rules the guards of restored placed records ask, over in-memory scenes, built the way the runner builds them.</summary>
/// <param name="TooClose">The too-close rule with the given settings over a world and its caches, in which the given objects cause no removals.</param>
/// <param name="TooCloseToWhatStays">The run's too-close rule over the worlds of <paramref name="WorldCaches"/>.</param>
/// <param name="FollowUpOver">The removal steps' follow-up over a scene of the run, with the given follow-up settings.</param>
internal sealed record TestPlacedGuardRules(
    IObjectCachesOfWorlds WorldCaches,
    Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> TooClose,
    ITooCloseToWhatStaysRule TooCloseToWhatStays,
    Func<CollectedObjects, AlsoRemoveSettings, IFollowUpOfObjectsLeaving> FollowUpOver)
{
    /// <summary>The run's default follow-up: what loses half its support, within the default touch gap.</summary>
    public static AlsoRemoveSettings DefaultFollowUp { get; } = new(FollowUpRemovalMode.OnlyWhatLosesItsSupport, 8f, 0.5f, TouchChainsShown: false);

    /// <param name="support">The touch gap and lost share of the support test.</param>
    public static TestPlacedGuardRules Create(
        IBaseObjectShapes shapes, ModTrustTable trust, LoadOrderPlugins plugins, OtherModsObjectsSettings support, ITerrainHeights terrain, int threads)
    {
        var triangles = new ResidentMeshTrees(shapes.ReadTriangles);
        var worldCaches = new TestObjectCachesOfWorlds(world => TestScenes.Index(world, shapes, threads));
        var causes = RemovalCauses.CreateCausesOfWorlds(worldCaches);
        var noWorld = TestScenes.Index(TestScenes.CreateWorld([], []), shapes, threads);
        var runCaches = new TestRunCaches(shapes, triangles, terrain, TestBaseFacts.Unresolved, objects: noWorld);
        var execution = new Execution(threads);
        return new TestPlacedGuardRules(
            worldCaches,
            (world, caches, excluded, settings) => TooCloseRules.CreatePlacementRule(
                new(world.Targets, caches.VisibleTargets, causes.Of(world, excluded, settings.Npcs), caches.PlacedNpcs, shapes, triangles), settings),
            TooCloseRules.CreateTooCloseToWhatStaysRule(runCaches, worldCaches, causes),
            (scene, alsoRemove) =>
            {
                var sceneCaches = new TestRunCaches(shapes, triangles, terrain, TestBaseFacts.Unresolved, objects: TestScenes.Index(scene, shapes, threads));
                var judging = new SupportJudging(scene.Targets, sceneCaches, trust, plugins, support, RestingObjectsRules.CreateSupportContacts(sceneCaches), execution);
                return RestingObjectsRules.CreateFollowUpOfObjectsLeaving(judging, alsoRemove, RestingObjectsRules.CreateAlsoRemoveRoundsAfter(alsoRemove, execution, sceneCaches));
            });
    }

    /// <summary>The guard rules over a scene of the run, with the given follow-up settings or the run's default.</summary>
    public WhereRemovedRestorer.GuardRules Over(
        CollectedObjects scene, ITooCloseToWhatStaysRule? tooClose = null, INavmeshCheckIfWritten? navmeshCheck = null, AlsoRemoveSettings? alsoRemove = null) =>
        new(tooClose ?? TooCloseRule(), FollowUpOver(scene, alsoRemove ?? DefaultFollowUp), navmeshCheck ?? TestNavmeshCheck.NoFault);

    /// <param name="tooClose">Builds the rule over a world and its caches; the run's rule when null.</param>
    public ITooCloseToWhatStaysRule TooCloseRule(Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = null) =>
        tooClose is null ? TooCloseToWhatStays : new TestTooCloseToWhatStays(WorldCaches, tooClose, TooCloseToWhatStays);
}

/// <summary>The object caches of in-memory worlds, each built on its first ask, as the run builds them.</summary>
internal sealed class TestObjectCachesOfWorlds(Func<CollectedObjects, IObjectCaches> index) : IObjectCachesOfWorlds
{
    private readonly ComputedOncePerKey<CollectedObjects, IObjectCaches> _built = new(Publication.BuiltOnce, ReferenceEqualityComparer.Instance);

    public IObjectCaches Of(CollectedObjects world) => _built.Get(world, () => index(world));
}

/// <summary>
/// A too-close rule over a world of what stays, built over the world's caches, which also finds every clash; the reach questions go
/// to the run's rule.
/// </summary>
internal sealed class TestTooCloseToWhatStays(
    IObjectCachesOfWorlds worldCaches, Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> create, ITooCloseToWhatStaysRule run)
    : ITooCloseToWhatStaysRule
{
    public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings) => create(world, worldCaches.Of(world), leftOut, settings);

    public TooCloseClashes FindEveryClash(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings, TargetObject target) =>
        TooCloseRules.FindEveryClash(excluded => Create(world, excluded, settings), leftOut, target);

    public Box ZoneReachOf(TargetObject placed, TooCloseSettings settings) => run.ZoneReachOf(placed, settings);

    public Box BoundsOf(OtherObject other) => run.BoundsOf(other);

    public ImmutableArray<int> FindObjectsReaching(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others) =>
        run.FindObjectsReaching(objects, others);

    public ImmutableArray<int> FindOthersReached(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others) =>
        run.FindOthersReached(objects, others);
}

internal sealed class TestNavmeshCheck(Func<NavmeshWriteTrial, NavmeshFault?> findFault) : INavmeshCheckIfWritten
{
    public static TestNavmeshCheck NoFault { get; } = new(_ => null);

    public NavmeshFault? FindFaultIfWritten(NavmeshWriteTrial trial) => findFault(trial);
}

/// <summary>Plugin records that answer only the land questions of the restore step.</summary>
/// <param name="readHeights">The heights of a cell's LAND record just below the cleaned mod's version and at the winning one.</param>
internal sealed class TestLandPlugin(Func<RecordKey, RecordKey?> findLandWorldspace, Func<ExteriorCell, LandHeights> readHeights) : IPluginRecords
{
    /// <summary>Every space uses its own land, and no cell has heights.</summary>
    public static TestLandPlugin Flat { get; } = new(space => space, _ => new LandHeights(null, null));

    public RecordKey? FindLandWorldspace(RecordKey space) => findLandWorldspace(space);

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => readHeights(cell).Earlier;

    public float[]? ReadTerrain(ExteriorCell cell) => readHeights(cell).Current;

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => throw new NotSupportedException();

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> records, IReadOnlySet<PluginName> linkedPlugins) => throw new NotSupportedException();

    public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

    public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();

    public ExteriorCell? FindExteriorCell(RecordKey landOrCell) => throw new NotSupportedException();

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
