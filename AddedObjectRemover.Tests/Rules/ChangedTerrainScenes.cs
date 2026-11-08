using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The terrain, the plugins and the helpers that the tests of the changed-terrain step build their scenes from.</summary>
internal static class ChangedTerrainScenes
{
    public const float Ground = 0f;
    public const float Raised = 64f;

    /// <summary>The height tolerance of <see cref="Settings"/>, and the smallest height step terrain heights come in.</summary>
    public const float Tolerance = 16f;
    public const float HeightStep = TerrainGrid.UnitsPerHeightStep;
    public const int VertexCount = TerrainGrid.VertexCount;

    /// <summary>The share of an area's vertices whose terrain type must change in <see cref="Settings"/>: 4 of the 16 at <see cref="WideSpot"/>.</summary>
    public const int TypePercent = 25;
    public const int ChangesAtTypeShare = 4;
    public const int AnyChange = 0;
    public const int WholeArea = 100;
    public const int MostChanges = 3;
    public const int FewerChanges = 2;
    private const float FullOpacity = 1f;

    /// <summary>A terrain vertex inside the check area of an object at <see cref="Spot"/>, and one far outside it.</summary>
    public const int Near = 8 * TerrainGrid.VerticesPerSide + 8;
    public const int Far = 20 * TerrainGrid.VerticesPerSide + 20;

    /// <summary>The vertex next to <see cref="Near"/>, also inside the check area (column 9 of row 8).</summary>
    public const int Beside = Near + 1;

    public static readonly Vector3 Spot = new(1000, 1000, 0);

    /// <summary>The 64-unit area of an object here covers 4 by 4 vertices from column and row 7 of the cell, all inside its bottom left quadrant.</summary>
    public static readonly Vector3 WideSpot = new(1088, 1088, 0);
    private const int WideSpotFirstVertex = 7;
    private const int WideSpotVerticesPerSide = 4;

    public static readonly (int Column, int Row)[] WideSpotVertices =
    [
        .. Enumerable.Range(WideSpotFirstVertex, WideSpotVerticesPerSide)
            .SelectMany(row => Enumerable.Range(WideSpotFirstVertex, WideSpotVerticesPerSide).Select(column => (column, row))),
    ];
    public static readonly RecordKey Worldspace = TestTargets.Space;
    public static readonly RecordKey Interior = TestTargets.SpaceKey(0x101);
    public static readonly RecordKey ChildWorldspace = TestTargets.SpaceKey(0x102);
    public static readonly ExteriorCell Cell = new(Worldspace, 0, 0);

    public static readonly PluginName BaseGame = new("Skyrim.esm");
    public static readonly PluginName Master = new("Master.esm");
    public static readonly PluginName Early = new("Early.esp");
    public static readonly PluginName Target = TestTargets.TargetName;
    public static readonly PluginName RequiringMod = new("RequiringMod.esp");
    public static readonly PluginName Other = new("Other.esp");
    public static readonly PluginName CompatibilityPatch = new("CompatibilityPatch.esp");
    public static readonly PluginName TrustedMod = new("TrustedMod.esp");
    public static readonly PluginName OwnChanges = new("OwnChanges.esp");
    public static readonly PluginName Later = new("Later.esp");
    public static readonly PluginName Patch = new("Synthesis.esp");

    public static readonly BaseKey Rock = new(new RecordKey(Target, 0x900), BaseLinkKind.PlaceableObject);
    public static readonly BaseKey Marker = new(new RecordKey(Target, 0x901), BaseLinkKind.PlaceableObject);
    public static readonly BaseKey Broken = new(new RecordKey(Target, 0x902), BaseLinkKind.PlaceableObject);

    /// <summary>
    /// The target's masters are the base game and Master.esm. TrustedMod.esp is trusted, and so are the two plugins that require the
    /// target, as the rule for such mods trusts them; OwnChanges.esp is trusted for its own changes; Early.esp loads before the
    /// target and is untrusted.
    /// </summary>
    public static readonly ModTrustTable Mods = TestModTrust.Of(
        Target,
        Patch,
        trusted: [BaseGame, Master, TrustedMod, RequiringMod, CompatibilityPatch],
        untrusted: [Early, Other, Later],
        trustedForOwnChanges: [OwnChanges]);

    private static readonly ImmutableArray<PluginName> TargetMasters = [BaseGame, Master];

    private static readonly PluginName[] Plugins = [BaseGame, Master, Early, Target, RequiringMod, Other, CompatibilityPatch, TrustedMod, OwnChanges, Later, Patch];

    public static readonly LoadOrderPlugins LoadOrder = TestLoadOrderPlugins.Of(
        new KnownPluginNames(Plugins), Patch, [.. Plugins.Select(plugin => new PluginListing(plugin, true, plugin == Target ? TargetMasters : []))]);

    public const float Deep = -1000f;

    public static ChangedTerrainSettings Settings(
        TerrainCheckedObjects objects = TerrainCheckedObjects.VisibleObjectsOnTheGround,
        float tolerance = Tolerance,
        bool bySize = false,
        float multiplier = 0.5f,
        bool byDistance = true,
        bool heights = true,
        bool types = true,
        int typePercent = TypePercent,
        bool waterHeights = true,
        float waterTolerance = Tolerance,
        bool waterAddedOrRemoved = true,
        int waterPercent = TypePercent) =>
        new(
            On: true, objects, heights, tolerance, types, typePercent, waterHeights, waterTolerance, waterAddedOrRemoved, waterPercent,
            bySize, multiplier, byDistance, AreaDistanceInUnits: 64f);

    public static float[] Heights(float height, params (int Vertex, float Height)[] changes)
    {
        var heights = Enumerable.Repeat(height, VertexCount).ToArray();
        foreach (var (vertex, changed) in changes) heights[vertex] = changed;
        return heights;
    }

    public static TerrainVersion Flat(PluginName plugin) => new(plugin, Heights(Ground), Layers: null);

    public static TerrainVersion RaisedAt(PluginName plugin, int vertex, float height = Raised) => new(plugin, Heights(Ground, (vertex, height)), Layers: null);

    public static TargetObject Placed(int index, BaseKey baseKey, Vector3? position = null, RecordKey? space = null) =>
        TestTargets.Create(index, TestTargets.At(position ?? Spot), baseKey, space ?? Worldspace);

    public static ChangedTerrainPass Find(
        IReadOnlyDictionary<ExteriorCell, TerrainVersion[]> terrain, ChangedTerrainSettings settings, int threads = 1, params TargetObject[] targets) =>
        Find(new TerrainPlugin(terrain), settings, threads, targets);

    public static ChangedTerrainPass Find(TerrainPlugin plugin, ChangedTerrainSettings settings, int threads = 1, params TargetObject[] targets)
    {
        var world = TestScenes.CreateWorld(targets.Length == 0 ? [Placed(0, Rock)] : targets, []);
        return Find(world, Mods.AcceptedWorld, plugin, threads, TestJudgedGroups.OfAll(world.Targets, settings)).Passes.Single();
    }

    public static ChangedTerrainPass FindInCell(params TerrainVersion[] versions) =>
        Find(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = versions }, Settings());

    /// <param name="changedBy">Null for a change only the patch holds.</param>
    public static ProposedRemoval Removal(int index, PluginName? changedBy, TerrainChangeKind kind = TerrainChangeKind.Height) =>
        new(new TargetId(index), new TerrainChanged(changedBy, kind));

    public static ChangedTerrainResult Find(
        CollectedObjects world, AcceptedWorld accepted, IPluginRecords plugin, int threads, params ObjectsJudgedWith<ChangedTerrainSettings>[] groups) =>
        Decide(world, accepted, plugin, threads, groups).Result;

    /// <summary>The step over the world's targets, none of them kept, decided before.</summary>
    public static Decided<ChangedTerrainResult> Decide(
        CollectedObjects world, AcceptedWorld accepted, IPluginRecords plugin, int threads, params ObjectsJudgedWith<ChangedTerrainSettings>[] groups) =>
        DecideAfterEarlierRuns(world, accepted, plugin, [], threads, groups);

    /// <param name="restored">The parts earlier runs gave back.</param>
    public static Decided<ChangedTerrainResult> DecideAfterEarlierRuns(
        CollectedObjects world, AcceptedWorld accepted, IPluginRecords plugin, ImmutableArray<RestoredPart> restored, int threads, params ObjectsJudgedWith<ChangedTerrainSettings>[] groups) =>
        ChangedTerrainObjects.Find(
            new(
                world,
                TestSeededDecisions.StartUnprotected(world.Targets),
                ChangedTerrainRules.CreateChecks(world.Targets, accepted, LoadOrder, ChangedTerrainRules.ViewOfLoadOrder(plugin, restored), new BoxShapes()),
                new Execution(threads),
                groups));

    private static readonly RecordKey GrassMaterial = new(BaseGame, 0x10);
    private static readonly RecordKey ThickGrassMaterial = new(Other, 0x20);
    private static readonly RecordKey StoneMaterial = new(BaseGame, 0x30);

    public static readonly RecordKey Grass = new(BaseGame, 0xA00);
    public static readonly RecordKey OtherGrass = new(BaseGame, 0xA01);
    public static readonly RecordKey ThickGrass = new(Other, 0xA02);
    public static readonly RecordKey Road = new(BaseGame, 0xA03);
    public static readonly RecordKey Unresolved = new(BaseGame, 0xA04);
    public static readonly RecordKey OtherUnresolved = new(BaseGame, 0xA05);

    /// <summary>Two grass textures, a mod's grass whose material is a child of grass, and a road on stone.</summary>
    public static readonly Dictionary<RecordKey, RecordKey[]> Materials = new()
    {
        [Grass] = [GrassMaterial],
        [OtherGrass] = [GrassMaterial],
        [ThickGrass] = [ThickGrassMaterial, GrassMaterial],
        [Road] = [StoneMaterial],
    };

    /// <summary>Every quadrant painted with the base texture, and optionally one alpha layer of the same opacity everywhere.</summary>
    public static TerrainLayers Painted(RecordKey baseTexture, RecordKey? alpha = null, float opacity = 1f) =>
        new([.. Enumerable.Range(0, TerrainQuadrants.Count).Select(_ => new QuadrantLayers(
            baseTexture,
            alpha is { } texture ? [new TerrainAlphaLayer(texture, LayerNumber: 0, Enumerable.Repeat(opacity, TerrainQuadrants.VertexCount).ToArray())] : []))]);

    public static TerrainVersion FlatPainted(PluginName plugin, TerrainLayers? layers) => new(plugin, Heights(Ground), layers);

    public static TerrainPlugin PaintedCell(params TerrainVersion[] versions) =>
        new(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = versions }, Materials);

    /// <summary>Grass everywhere, with road painted over the first <paramref name="count"/> vertices of the area of an object at <see cref="WideSpot"/>.</summary>
    public static TerrainLayers RoadAtWideSpot(int count) => RoadInFirstQuarter(WideSpotVertices.Take(count));

    /// <summary>As <see cref="RoadAtWideSpot"/>, with road also at the bottom left corner of the quarter, outside the area.</summary>
    public static TerrainLayers RoadAtWideSpotAndQuarterCorner(int count) => RoadInFirstQuarter([.. WideSpotVertices.Take(count), (0, 0)]);

    /// <summary>Grass everywhere, with road painted over the given vertices of the bottom left quarter.</summary>
    private static TerrainLayers RoadInFirstQuarter(IEnumerable<(int Column, int Row)> vertices)
    {
        var opacities = new float[TerrainQuadrants.VertexCount];
        foreach (var (column, row) in vertices) opacities[row * TerrainQuadrants.VerticesPerSide + column] = FullOpacity;
        var quadrants = Painted(Grass).Quadrants;
        quadrants[TerrainQuadrants.IndexOf(0, 0)] = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Road, LayerNumber: 0, opacities)]);
        return new TerrainLayers(quadrants);
    }

    /// <summary>A rock 20 units wide and high, a marker without size, and a base whose visibility cannot be read.</summary>
    public sealed class BoxShapes : IBaseObjectShapes
    {
        public BaseShape Of(BaseKey? baseKey) =>
            baseKey == Marker ? new BaseShape(Box.Zero, null, InvisibleObjectKind.XMarkers) : new BaseShape(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 20)), null, null);

        public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker) =>
            baseKey == Broken ? throw new PlantedUserDataException("broken base")
            : baseKey == Marker ? ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers)
            : ObjectVisibility.Visible;

        public bool LayerBlocksActors(uint collisionLayer) => throw new NotSupportedException();

        public IReadOnlyList<MeasuredBase> Computed() => [];

        public BoundsStats CountBounds() => throw new NotSupportedException();
    }

    /// <summary>
    /// Terrain and water versions by cell, and material chains by texture; only the test worldspace has land data, which its child
    /// worldspace uses.
    /// </summary>
    public sealed class TerrainPlugin(
        IReadOnlyDictionary<ExteriorCell, TerrainVersion[]> terrain,
        IReadOnlyDictionary<RecordKey, RecordKey[]>? materials = null,
        IReadOnlyDictionary<ExteriorCell, WaterCopies>? water = null,
        IReadOnlyDictionary<RecordKey, ExteriorCell>? cellsOfRecords = null) : IPluginRecords
    {
        public ExteriorCell? FindExteriorCell(RecordKey landOrCell) => cellsOfRecords?.TryGetValue(landOrCell, out var cell) == true ? cell : null;

        private int _materialReads;
        private int _waterReads;

        public int MaterialReads => _materialReads;

        public int WaterReads => _waterReads;

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => terrain.GetValueOrDefault(cell) ?? [];

        public WaterCopies ReadWaterCopies(ExteriorCell cell)
        {
            Interlocked.Increment(ref _waterReads);
            return water?.GetValueOrDefault(cell) ?? new WaterCopies([], []);
        }

        public RecordKey? FindLandWorldspace(RecordKey space) => space == Worldspace || space == ChildWorldspace ? Worldspace : null;

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture)
        {
            Interlocked.Increment(ref _materialReads);
            return materials?.GetValueOrDefault(texture) ?? [];
        }

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => throw new NotSupportedException();

        public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) => throw new NotSupportedException();

        public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

        public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => throw new NotSupportedException();

        public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
