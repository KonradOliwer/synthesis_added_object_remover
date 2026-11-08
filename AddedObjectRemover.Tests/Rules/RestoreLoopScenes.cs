using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The in-memory scenes of the guards between the kinds that go back where the cleaned mod was removed, repeated until nothing more
/// changes: the cleaned mod raised the base game's flat ground of cell (0, 0) by 80 units and every one of its objects around the cell
/// was removed, so every area there is cleared. Its moved rocks, an edited navmesh, the cell's water level and the ground go back
/// unless what stays keeps them.
/// </summary>
internal static class RestoreLoopScenes
{
    public const float Raised = 80f;
    public const float RockHalfSize = 20f;
    public const float TooCloseDistance = 10f;
    public const float QuarterCentre = ExteriorGrid.CellSize / 4;
    public const float CellCentre = ExteriorGrid.CellSize / 2;
    private const float NavmeshHalfSize = 100f;
    public const float EarlierWater = 50f;
    public const float CleanedModsWater = -100f;
    public const int Side = TerrainGrid.VerticesPerSide;

    public static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreLoopShapes.esp");
    public static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreloop\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    public static readonly TestStatic House = new(new FormKey(ShapeMod, 0x802), @"test\restoreloop\house.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100, -100, 0), new Vector3(100, 100, 100))));
    public static readonly TestStatic Tree = new(new FormKey(ShapeMod, 0x803), @"test\restoreloop\tree.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 300))));

    /// <summary>An invisible wall: a mesh with collision that stops characters and nothing to render.</summary>
    public static readonly TestStatic CollisionWall = new(
        new FormKey(ShapeMod, 0x804), @"test\restoreloop\collisionwall.nif", [], NiflySharp.Enums.SkyrimLayer.SKYL_STATIC, new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize)));

    /// <summary>A base with the engine's marker flag and nothing to render, as a collision box primitive has.</summary>
    public static readonly TestStatic MarkerBase = new(
        new FormKey(ShapeMod, 0x805), @"test\restoreloop\markerbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)), IsMarker: true);

    /// <summary>A mist: a mesh drawn only with an effect shader, which blocks no one.</summary>
    public static readonly TestStatic Mist = new(
        new FormKey(ShapeMod, 0x806), @"test\restoreloop\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))), EffectOnly: true);

    /// <summary>A log lying east to west, long enough to reach over several grid points.</summary>
    public static readonly TestStatic Log = new(new FormKey(ShapeMod, 0x807), @"test\restoreloop\log.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-300, -20, 0), new Vector3(300, 20, 40))));

    public const string ShapeDataFolder = "RestoreLoopData";

    public static readonly TestStatic[] Statics = [Block, House, Tree, CollisionWall, MarkerBase, Mist, Log];

    public static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(
        TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE)), ShapeDataFolder, Statics));

    public static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    public static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    public static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, LaterMod.ToPluginName()];
    public static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    public static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []);
    public static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap: 1f, SupportLostFraction: 0.5f);

    public static readonly WhereRemovedRestoreSettings AllOn = new(
        OtherModsObjects: true,
        Terrain: true,
        Cells: true,
        Navmeshes: true,
        new ClearedArea(SearchRadius: 1000f, MaxStandingSizePercent: 0.1, MaxStandingCountPercent: 0.1),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never));

    public static readonly RecordKey WaterCell = new(BaseGame, 0x700);
    public static readonly RecordKey NavmeshCell = new(BaseGame, 0x701);
    public static readonly RecordKey NavmeshKey = new(BaseGame, 0x702);

    /// <summary>A later mod's house stands on the raised ground over the navmesh's earlier place.</summary>
    public static readonly Vector3 HousePlace = new(1000, 2000, Raised);

    /// <summary>The cleaned mod moved the navmesh from under the house to here.</summary>
    public static readonly Vector3 NavmeshNow = new(2600, 2600, 0);

    public static readonly Vector3 RockNowPlace = new(3500, 1500, Raised + RockHalfSize);

    /// <summary>The base game trusted in full, as the base game always is.</summary>
    public static readonly ModTrustTable BaseGameTrusted = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [], baseGame: [BaseGame]);

    public const string StoppedCheck = "InvalidOperationException: broke";

    public static float[] HeightsAll(float height) => [.. Enumerable.Repeat(height, TerrainGrid.VertexCount)];

    /// <summary>The ground as it is: cell (0, 0) at the given height everywhere.</summary>
    public static TerrainHeights GroundAll(float height) => new(cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? HeightsAll(height) : null, space => space);

    /// <summary>The ground as it is: cell (0, 0) raised as <see cref="RaisedLand"/> makes it.</summary>
    public static TerrainHeights RaisedGround() => GroundAll(Raised);

    /// <summary>
    /// The cleaned mod moved a base game rock from <paramref name="oldPlace"/> (by default the cleared south-west quarter of cell
    /// (0, 0), raised over the water levels) to <paramref name="copyPlace"/> (by default its south-east quarter), beside an object of its
    /// own that stays 300 units north, so the rock goes back with a copy left there.
    /// </summary>
    public static Scene CopyScene(ChangedTerrainSettings? cleanedModsTerrainCheck, Vector3? copyPlace = null, Vector3? oldPlace = null)
    {
        var now = copyPlace ?? RockNowPlace;
        var rock = Rock(0, oldPlace ?? new Vector3(1500, 1500, 500), now) with { Key = FirstStandingTargetKey(), Target = new TargetId(RemovedTargets().Count) };
        return new Scene
        {
            Rocks = [rock],
            Standing = [now],
            OwnStanding = [now + new Vector3(0, 300, 0)],
            Bases = TestBaseFacts.PlainScenery,
            CleanedModsTerrainCheck = cleanedModsTerrainCheck,
        };
    }

    /// <summary>A later mod's crate stands next to the rock's old place when <paramref name="present"/>, so the rock is refused.</summary>
    public static List<OtherObject> CrateBesideTheRock(Vector3 oldPlace, bool present) =>
        present ? [LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0))] : [];

    public static Scene RockInTheNavmeshTakenBack() => WithAHouseKeepingItsGround(new()
    {
        Rocks = [Rock(0, NavmeshNow + new Vector3(0, 0, RockHalfSize / 2), RockNowPlace)],
        Others = [RockNow(0, RockNowPlace)],
        Navmeshes = [MovedNavmesh()],
    });

    /// <summary>
    /// The scene with a base game house standing on the raised ground at <see cref="HousePlace"/>, numbered after its other objects, and
    /// KeepSupportingObject: the ground under the house stays once the plan would lower it.
    /// </summary>
    public static Scene WithAHouseKeepingItsGround(Scene scene) => scene with
    {
        Others = [.. scene.Others, TestShapes.Placed(BaseGameMod, 10, House.Base, HousePlace)],
        Ground = RaisedGround(),
        Support = Support with { Handling = OtherModsObjectsHandling.KeepSupportingObject },
        Trust = BaseGameTrusted,
    };

    /// <summary>A scene that takes several passes and holds a navmesh, a house, a tree, a rock and a water cell.</summary>
    public static Scene SceneOfSeveralPasses() => RockInTheNavmeshTakenBack() with
    {
        Others = [.. RockInTheNavmeshTakenBack().Others, TestShapes.Placed(BaseGameMod, 11, Tree.Base, new Vector3(3000, 1000, 0)), LaterObject(12, Block.Base, new Vector3(CellCentre + 300, CellCentre, 0))],
        Cells = [ChangedWater()],
    };

    public static int PointAt(int x, int y) => y * Side + x;

    /// <summary>The cleaned mod's removed objects: at every quarter centre of cell (0, 0) and its neighbour to the east, and at the centre of cell (0, 0).</summary>
    public static List<TargetObject> RemovedTargets()
    {
        List<Vector3> places = [new(CellCentre, CellCentre, Raised)];
        for (var cellX = 0; cellX <= 1; cellX++)
        {
            foreach (var (quarterX, quarterY) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                places.Add(new Vector3(cellX * ExteriorGrid.CellSize + (2 * quarterX + 1) * QuarterCentre, (2 * quarterY + 1) * QuarterCentre, Raised));
            }
        }
        return [.. places.Select((place, index) => TestTargets.Create(index, TestTargets.At(place), Block.Base, TestTargets.Space))];
    }

    /// <summary>A base game object the cleaned mod moved, which no round removes, so it stands where it is.</summary>
    public static TargetObject StandingTarget(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space) with { Origin = TargetOrigin.Moved };

    /// <summary>The key of the first object of <see cref="Scene.Standing"/>.</summary>
    public static RecordKey FirstStandingTargetKey() => TestTargets.Key(RemovedTargets().Count);

    /// <summary>The cleaned mod raised every point of the base game's flat ground of cell (0, 0).</summary>
    public static LandChangedByCleanedMod RaisedLand()
    {
        var all = PointSet.Of(Enumerable.Range(0, TerrainGrid.VertexCount));
        return new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 0, 0),
            new RecordKey(BaseGame, 0x600),
            all,
            PointSet.Of([]),
            default,
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            new LandHeights([.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(Raised, TerrainGrid.VertexCount)]));
    }

    /// <summary>Cell (0, 0), whose music and water level the cleaned mod changed: it lowered the water from 50 to −100.</summary>
    public static CellChangedByCleanedMod ChangedWater() => new(
        WaterCell,
        TestTargets.Space,
        (0, 0),
        [new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()), new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.Undo())],
        LinkedFromCleanedMod: false,
        TestTargets.TargetName,
        new CellWaterCopy(BaseGame, HasWater: true, EarlierWater),
        new CellWaterCopy(TestTargets.TargetName, HasWater: true, CleanedModsWater),
        DefaultWaterLevel: null);

    /// <summary>A base game navmesh, a flat square under the house, that the cleaned mod moved to <see cref="NavmeshNow"/>.</summary>
    public static NavmeshChangedByCleanedMod MovedNavmesh() => new(
        NavmeshKey,
        TestTargets.Space,
        NavmeshCell,
        TestTargets.TargetName,
        NavmeshCase.Edited,
        new PartOutcome.Undo(),
        new PartOutcome.Unchanged(),
        new PartOutcome.Unchanged(),
        Square(BaseGame, HousePlace with { Z = 0 }),
        Square(TestTargets.TargetName, NavmeshNow),
        ReplacedBy: [],
        Neighbours: [],
        Doors: [],
        NamedInPreferredPaths: false,
        WinningMapMod: null,
        CellDeletedInAVersion: false,
        LaterWinner: null,
        WinningMapEntry: null);

    public static NavmeshShape Square(PluginName plugin, Vector3 centre) => new(
        plugin,
        NavmeshCell,
        IsDeleted: false,
        [
            centre + new Vector3(-NavmeshHalfSize, -NavmeshHalfSize, 0), centre + new Vector3(NavmeshHalfSize, -NavmeshHalfSize, 0),
            centre + new Vector3(NavmeshHalfSize, NavmeshHalfSize, 0), centre + new Vector3(-NavmeshHalfSize, NavmeshHalfSize, 0),
        ],
        [new NavmeshTriangleFact(0, 1, 2, Preferred: false), new NavmeshTriangleFact(0, 2, 3, Preferred: false)],
        [],
        [],
        MapEntry: null);

    /// <summary>
    /// A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/>, whose position goes back;
    /// <paramref name="removedNow"/> when the cleaned mod also disabled it, so it stands nowhere now (see <see cref="Disabled"/>).
    /// </summary>
    public static RecordChangedByCleanedMod Rock(int index, Vector3 oldPlace, Vector3 now, bool removedNow = false) =>
        new(
            new FormKey(BaseGameMod, 0x900 + (uint)index).ToRecordKey(),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            Block.Base,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f), Block.Base) with { Undo = removedNow ? PlacedParts.Position | PlacedParts.Presence : PlacedParts.Position },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    /// <summary>The winning presence of records the game does not have.</summary>
    public static Dictionary<RecordKey, PlacedPresence> Disabled(params RecordKey[] records) =>
        records.ToDictionary(record => record, _ => new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false));

    /// <summary>The rock where the cleaned mod put it: its override is a change in the world without the cleaned mod.</summary>
    public static OtherObject RockNow(int index, Vector3 now) =>
        TestShapes.Placed(BaseGameMod, index, Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };

    public static OtherObject LaterObject(int index, BaseKey model, Vector3 position) =>
        TestShapes.Placed(LaterMod, index, model, position) with { ChangeWithoutCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null) };

    public static RestoreWhereRemovedResult Plan(Scene scene) => WhereRemovedRestorer.Plan(InputOf(scene));

    /// <summary>
    /// The targets and the other plugins' objects as the collector sorts them: a base game object no plugin changed shows its accepted
    /// values, so it is support only, numbered after the other-mod objects; every other one is an other-mod object.
    /// </summary>
    public static CollectedObjects WorldOf(ImmutableArray<TargetObject> targets, List<OtherObject> others)
    {
        List<OtherObject> causes = [.. others.Where(other => !IsSupportOnly(other))];
        List<OtherObject> supportOnly = [.. others.Where(IsSupportOnly).Select(other => other with { Change = null })];
        List<OtherObject> numbered = [.. causes.Concat(supportOnly).Select((other, index) => other with { Id = new OtherId(index) })];
        return TestScenes.CreateWorld(targets, [.. numbered.Take(causes.Count)], [.. numbered.Skip(causes.Count)]);
    }

    private static bool IsSupportOnly(OtherObject other) => other.WinningMod == BaseGame && other.ChangeWithoutCleanedMod is null;

    public static WhereRemovedRestorer.Input InputOf(Scene scene)
    {
        var removedTargets = RemovedTargets();
        var firstOwn = removedTargets.Count + scene.Standing.Count;
        ImmutableArray<TargetObject> targets =
        [
            .. removedTargets,
            .. scene.Standing.Select((position, index) => StandingTarget(removedTargets.Count + index, position) with { Base = scene.StandingModel }),
            .. scene.OwnStanding.Select((position, index) => TestTargets.Create(firstOwn + index, TestTargets.At(position), Block.Base, TestTargets.Space)),
        ];
        var collected = WorldOf(targets, scene.Others) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedCells = [.. scene.Cells],
            ChangedLands = [.. scene.Lands.Select(land => land with { EarlierPlugin = scene.EarlierLandPlugin })],
            ChangedNavmeshes = [.. scene.Navmeshes],
            WinningPresence = scene.Presence,
            WinningPrimitives = scene.Primitives,
            CleanedModsObjectsOverriddenLater = [.. scene.OverriddenLater],
        };
        var rules = TestPlacedGuardRules.Create(scene.Shapes, scene.Trust, Plugins, scene.Support, scene.Ground, scene.Workers);
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = (scene.CheckedWorlds, scene.TooCloseStopsFor) switch
        {
            ({ } worlds, _) => (world, caches, excluded, settings) =>
            {
                worlds.Enqueue(world);
                return rules.TooClose(world, caches, excluded, settings);
            },
            (_, { } stopsFor) => (world, caches, excluded, settings) => new StoppingForRule(rules.TooClose(world, caches, excluded, settings), stopsFor),
            _ => null,
        };
        ImmutableArray<NavmeshWorldTriangle> winning =
        [
            .. scene.Navmeshes.SelectMany(navmesh => Enumerable.Range(0, navmesh.Winner.Triangles.Length)
                .Select(triangle => new NavmeshWorldTriangle(navmesh.Navmesh, navmesh.Winner.CornersOf(triangle)!.Value))),
            .. scene.Winning,
        ];
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        var caches = new TestRunCaches(
            scene.Shapes,
            terrain: scene.Ground,
            bases: scene.Bases,
            navmeshes: new NavmeshIndex(bucket => bucket.Grid is null && !winning.IsEmpty ? [.. winning] : null),
            shownTargets: TestScenes.Create(targets, [], scene.Shapes).IndexShownTargets);
        var accepted = new AcceptedWorld(
            TestTargets.PatchName,
            ImmutableDictionary<PluginName, VersionTrust>.Empty
                .Add(BaseGame, VersionTrust.Full)
                .Add(TestTargets.TargetName, VersionTrust.Full)
                .Add(LaterMod.ToPluginName(), scene.Trust.IsTrustedInFull(LaterMod.ToPluginName()) ? VersionTrust.Full : VersionTrust.None));
        var terrain = new TestTerrainView(scene.ViewLands ?? scene.Lands, scene.Cells, scene.EarlierLandPlugin, TestTargets.TargetName, scene.LandLayers, scene.OtherWater);
        var groups = TestTargetGroups.None with
        {
            Accepted = accepted,
            ChangedTerrainRule = scene.TerrainRule ?? ChangedTerrainRules.CreateJudging(accepted, Plugins, terrain, scene.Shapes),
            CleanedModsObjects = TestTargetGroups.None.CleanedModsObjects with { ChangedTerrainSettings = scene.CleanedModsTerrainCheck ?? TestTargetGroups.ChangedTerrainOff },
            TooClose = scene.StandingTooClose is { } standingTooClose
                ? TestTargetGroups.SettingsOf(Enumerable.Range(removedTargets.Count, scene.Standing.Count).Select(index => new TargetId(index)), standingTooClose)
                : TestTargetGroups.None.TooClose,
        };
        return new(
            new(collected, scene.OtherMods),
            final,
            TestClearedAreas.Of(collected, final, caches, scene.Settings),
            rules.Over(collected, rules.TooCloseRule(tooClose), navmeshCheck: new TestNavmeshCheck(scene.FindFault)),
            scene.StandingTerrainCheck is { } terrainCheck
                ? groups with
                {
                    ChangedTerrain = TestTargetGroups.SettingsOf(
                        Enumerable.Range(removedTargets.Count, scene.Standing.Count).Select(index => new TargetId(index)), terrainCheck),
                }
                : groups,
            new(
                caches,
                new TestLandPlugin(space => space, scene.ReadHeights),
                Plugins,
                TestTargets.TargetName),
            new Execution(scene.Workers),
            scene.Settings,
            () => TimeSpan.Zero);
    }

    /// <summary>What the cleaned mod changed besides its own removed objects, and the other plugins' objects that stand now, numbered in this order.</summary>
    public sealed record Scene
    {
        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The places of base game objects the cleaned mod moved that no round removes, numbered after the removed objects.</summary>
        public List<Vector3> Standing { get; init; } = [];

        /// <summary>The base of the objects of <see cref="Standing"/>.</summary>
        public BaseKey StandingModel { get; init; } = Block.Base;

        /// <summary>The changed-terrain step's settings the objects of <see cref="Standing"/> are judged with; none judges them when null.</summary>
        public ChangedTerrainSettings? StandingTerrainCheck { get; init; }

        /// <summary>The places of the cleaned mod's own objects that no round removes, numbered after <see cref="Standing"/>.</summary>
        public List<Vector3> OwnStanding { get; init; } = [];

        /// <summary>The changed-terrain step's settings of the cleaned mod's own objects, and so of a copy; the step is off for them when null.</summary>
        public ChangedTerrainSettings? CleanedModsTerrainCheck { get; init; }

        /// <summary>What the bases are, which tells whether a moved object may be copied.</summary>
        public IBaseFacts Bases { get; init; } = TestBaseFacts.Unresolved;

        public List<OtherObject> Others { get; init; } = [];

        /// <summary>The cleaned mod's objects later plugins hold, each also among <see cref="Others"/> under the same key.</summary>
        public List<TargetObject> OverriddenLater { get; init; } = [];

        /// <summary>What became of other mods' objects left without support; their ids are those the collector gives them.</summary>
        public OtherModsObjectsResult OtherMods { get; init; } = OtherModsObjectsResult.SwitchedOff;

        /// <summary>The ground as it is that objects rest on; none by default.</summary>
        public ITerrainHeights Ground { get; init; } = TestGround.NoTerrain();

        public OtherModsObjectsSettings Support { get; init; } = RestoreLoopScenes.Support;

        public ModTrustTable Trust { get; init; } = RestoreLoopScenes.Trust;

        public List<LandChangedByCleanedMod> Lands { get; init; } = [RaisedLand()];

        /// <summary>The LAND records whose versions the changed-terrain rule reads; those of <see cref="Lands"/> when null.</summary>
        public List<LandChangedByCleanedMod>? ViewLands { get; init; }

        /// <summary>The plugin of the LAND records' version below the cleaned mod's, as the changed-terrain rule reads it.</summary>
        public PluginName EarlierLandPlugin { get; init; } = BaseGame;

        /// <summary>The changed-terrain rule; the shared rule over the scene's terrain when null.</summary>
        public IChangedTerrainJudging? TerrainRule { get; init; }

        /// <summary>The texture layers of a LAND record's version by its plugin, as the changed-terrain rule reads them; none by default.</summary>
        public Func<PluginName, TerrainLayers?> LandLayers { get; init; } = _ => null;

        public List<CellChangedByCleanedMod> Cells { get; init; } = [];

        /// <summary>The water copies, in load order, of cells whose water the cleaned mod did not change, as the changed-terrain rule reads them.</summary>
        public Dictionary<ExteriorCell, CellWaterCopy[]> OtherWater { get; init; } = new();

        public List<NavmeshChangedByCleanedMod> Navmeshes { get; init; } = [];

        /// <summary>The winning presence of the placed records the game does not have or that follow an enable parent.</summary>
        public Dictionary<RecordKey, PlacedPresence> Presence { get; init; } = new();

        /// <summary>The triangles of winning navmeshes the cleaned mod did not change.</summary>
        public List<NavmeshWorldTriangle> Winning { get; init; } = [];

        /// <summary>The heights of a cell's LAND record below the cleaned mod and at the winner, for cells without a LAND fact.</summary>
        public Func<ExteriorCell, LandHeights> ReadHeights { get; init; } = _ => new LandHeights(null, null);

        /// <summary>What the winning version of each primitive says about the reference itself.</summary>
        public Dictionary<RecordKey, ReferenceFacts> Primitives { get; init; } = new();

        /// <summary>The consistency check's first fault over what the trial's writes alone would leave; none by default.</summary>
        public Func<NavmeshWriteTrial, NavmeshFault?> FindFault { get; init; } = _ => null;

        /// <summary>The too-close step's settings the objects of <see cref="Standing"/> are judged with; none judges them when null.</summary>
        public TooCloseSettings? StandingTooClose { get; init; }

        /// <summary>When set, every too-close check of the placed record with this key stops unexpectedly.</summary>
        public RecordKey? TooCloseStopsFor { get; init; }

        /// <summary>When set, collects the world of every too-close rule built, once per rule.</summary>
        public ConcurrentQueue<CollectedObjects>? CheckedWorlds { get; init; }

        public WhereRemovedRestoreSettings Settings { get; init; } = AllOn;
        public int Workers { get; init; } = 1;

        /// <summary>The base object shapes and collision layers the guards read.</summary>
        public IBaseObjectShapes Shapes { get; init; } = RestoreLoopScenes.Shapes;
    }

    /// <summary>A too-close rule whose checks of the placed record with the given key stop unexpectedly.</summary>
    public sealed class StoppingForRule(IPlacementRule rule, RecordKey stopsFor) : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) => target.Key == stopsFor ? new Stopping() : rule.StartTries(target);

        private sealed class Stopping : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => new(Clash: null, StoppedCheck);

            public void Dispose()
            {
            }
        }
    }
}
