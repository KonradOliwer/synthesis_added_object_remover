using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The rules the guards of restored placed records ask, over in-memory scenes, built the way the runner builds them.</summary>
/// <param name="TooClose">The too-close rule with the given settings over a world and its caches, in which the given objects cause no removals.</param>
internal sealed record TestPlacedGuardRules(
    IObjectCachesOfWorlds WorldCaches,
    Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> TooClose,
    IHoldersOfObjectsThatStay HoldersOfObjectsThatStay,
    OtherModsObjectsSettings Support)
{
    /// <param name="support">The touch gap and lost share of the support test.</param>
    public static TestPlacedGuardRules Create(
        IBaseObjectShapes shapes, ModTrustTable trust, LoadOrderPlugins plugins, OtherModsObjectsSettings support, ITerrainHeights terrain, int threads)
    {
        var triangles = new TriangleStore(shapes.ReadTriangles);
        var surfaces = TestSurfaceSamples.Over(triangles);
        var worldCaches = new TestObjectCachesOfWorlds(world => TestScenes.Index(world, shapes, threads));
        return new TestPlacedGuardRules(
            worldCaches,
            (world, caches, excluded, settings) => TooCloseObjects.CreatePlacementRule(
                world.Targets,
                caches.VisibleTargets,
                RemovalCauseSelector.SelectForOldPlace(caches.OtherModObjects, world.OtherModObjects.Length, excluded, settings.Npcs),
                caches.PlacedNpcs,
                shapes,
                triangles,
                settings),
            OtherModsObjects.CreateHoldersOfObjectsThatStay(worldCaches, trust, plugins, support, shapes, triangles, surfaces, terrain, new Execution(threads)),
            support);
    }

    /// <param name="tooClose">Builds the rule over a world and its caches; <see cref="TooClose"/> when null.</param>
    public ITooCloseToWhatStaysRule TooCloseRule(Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = null) =>
        new TestTooCloseToWhatStays(WorldCaches, tooClose ?? TooClose);
}

/// <summary>The object caches of in-memory worlds, each built on its first ask, as the run builds them.</summary>
internal sealed class TestObjectCachesOfWorlds(Func<CollectedObjects, IObjectCaches> index) : IObjectCachesOfWorlds
{
    private readonly ComputedOncePerKey<CollectedObjects, IObjectCaches> _built = new(Publication.BuiltOnce, ReferenceEqualityComparer.Instance);

    public IObjectCaches Of(CollectedObjects world) => _built.Get(world, () => index(world));
}

/// <summary>A too-close rule over a world of what stays, built over the world's caches.</summary>
internal sealed class TestTooCloseToWhatStays(
    IObjectCachesOfWorlds worldCaches, Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> create) : ITooCloseToWhatStaysRule
{
    public IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings) => create(world, worldCaches.Of(world), leftOut, settings);
}

internal sealed class TestNavmeshCheck(Func<IReadOnlyList<WriteOrder>, NavmeshFault?> findFault) : INavmeshCheckIfWritten
{
    public static TestNavmeshCheck NoFault { get; } = new(_ => null);

    public NavmeshFault? FindFaultIfWritten(IReadOnlyList<WriteOrder> orders) => findFault(orders);
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

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => throw new NotSupportedException();

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> records, IReadOnlySet<PluginName> linkedPlugins) => throw new NotSupportedException();

    public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

    public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
