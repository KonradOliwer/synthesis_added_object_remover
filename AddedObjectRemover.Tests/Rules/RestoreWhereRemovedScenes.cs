using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using static AddedObjectRemover.Tests.Fixtures.TestOneRoundSteps;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The scene the tests of restoring where the cleaned mod was removed share: the cleaned mod's own blocks, the settings, the camp with
/// the rock it moved, and the builders that run the restore over in-memory target objects.
/// </summary>
internal static class RestoreWhereRemovedScenes
{
    public const float SearchRadius = 1000f;
    public const double TenthOfAPercent = 0.1;
    public const double BlockVolume = 40d * 40 * 40;
    public const double BigBlockVolume = 200d * 200 * 200;

    public static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreAreaShapes.esp");
    public static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restorearea\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    public static readonly TestStatic BigBlock = new(new FormKey(ShapeMod, 0x802), @"test\restorearea\bigblock.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));
    public static readonly TestStatic Flat = new(new FormKey(ShapeMod, 0x803), @"test\restorearea\flat.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 0))));
    /// <summary>A puff of fog: a mesh whose only shape is drawn with an effect shader, as big as <see cref="Block"/>.</summary>
    public static readonly TestStatic Mist = new(new FormKey(ShapeMod, 0x804), @"test\restorearea\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))), EffectOnly: true);

    /// <summary>A fog volume drawn only with an effect shader, far larger than every solid piece here.</summary>
    public static readonly TestStatic Fog = new(new FormKey(ShapeMod, 0x805), @"test\restorearea\fog.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-800), new Vector3(800))), EffectOnly: true);
    public static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "RestoreWhereRemovedData", Block, BigBlock, Flat, Mist, Fog));

    public static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    public static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    public static readonly PluginName Later = new("Later.esp");
    public static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    public static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);

    public static readonly WhereRemovedRestoreSettings RestoreSettings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: true,
        Navmeshes: false,
        new ClearedArea(SearchRadius, MaxStandingSizePercent: TenthOfAPercent, MaxStandingCountPercent: TenthOfAPercent),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: 10f, ZoneShape.BoundingBox, NpcHandling.Never),
        TouchGap: 1f);

    public static readonly WhereRemovedRestoreSettings HalfMayStand =
        RestoreSettings with { ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 50, MaxStandingCountPercent: 50) };

    /// <summary>The default limits, 1% by size and 5% by number.</summary>
    public static readonly WhereRemovedRestoreSettings AtTheDefaults =
        RestoreSettings with { ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5) };

    public static readonly RecordKey ExteriorCellKey = new(BaseGame, 0x500);

    public static readonly INavmeshes NoNavmeshes = TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>>());

    public static readonly GuardReach Reach = new(Shapes, GuardRules(workers: 1).TooCloseToWhatStays, RestoreSettings.TooClose, RestoreSettings.TouchGap);

    public static readonly Vector3 Camp = new(5000, 0, 0);

    public static readonly IBaseFacts PlainScenery = TestBaseFacts.PlainScenery;

    public static readonly VisibleLeftBehindSettings LookAround =
        new(new LeftBehindSurroundings(LookAround: 200f, MinOccupiedDirections: 1, DirectionClearedPercent: 50, ClearedDirectionsPercent: 50), MinObjectsAround: 1);

    /// <summary>
    /// The cleaned mod's object 0 near the rock's old place; at the camp its campfire 1 beside the rock 2 it moved there, and its
    /// objects 3 and 4 farther out; then <paramref name="more"/>.
    /// </summary>
    public static ImmutableArray<TargetObject> CampScene(params TargetObject[] more) => CampScene(TargetOrigin.Moved, more);

    public static ImmutableArray<TargetObject> CampScene(TargetOrigin rockOrigin, params TargetObject[] more) =>
    [
        Own(0, new Vector3(100, 0, 0)),
        Own(1, Camp + new Vector3(60, 0, 0)),
        Own(2, Camp) with { Origin = rockOrigin, Key = RockKey },
        Own(3, Camp + new Vector3(0, 150, 0)),
        Own(4, Camp + new Vector3(0, -150, 0)),
        .. more,
    ];

    /// <summary>The base game rock the cleaned mod moved into the camp of <see cref="CampScene(TargetObject[])"/>.</summary>
    public static readonly RecordKey RockKey = new(BaseGame, 0x900);

    /// <summary>The rock of <see cref="CampScene(TargetObject[])"/>, which goes back to the old place at the origin.</summary>
    public static RecordChangedByCleanedMod MovedRock(PlacedParts undo = PlacedParts.Position) => ChangedRock(RockKey.Id, Vector3.Zero, target: new TargetId(2), undo: undo);

    /// <summary>A block of Later.esp that stands now; its record is a change in the world without the cleaned mod.</summary>
    public static OtherObject LaterBlock(Vector3 position) =>
        TestShapes.Placed(ModKey.FromNameAndExtension("Later.esp"), 0, Block.Base, position) with { ChangeWithoutCleanedMod = new ObjectChange(Later, AcceptedFrom: null) };

    /// <summary>The cleaned mod's own objects judged too close within <paramref name="distance"/> units, with a changed-terrain check that finds nothing.</summary>
    public static CleanedModsObjectRules OwnTooClose(float distance) =>
        TestTargetGroups.None.CleanedModsObjects with { TooClose = RestoreSettings.TooClose with { DistanceInUnits = distance } };

    /// <summary>A navmesh the cleaned mod added, with one triangle 20 units wide around <paramref name="centre"/>.</summary>
    public static NavmeshChangedByCleanedMod AddedNavmeshAround(Vector3 centre)
    {
        var cell = new FormKey(TestTargets.TargetMod, 0x601).ToRecordKey();
        var triangle = new NavmeshShape(
            TestTargets.TargetName,
            cell,
            IsDeleted: false,
            [centre + new Vector3(-10, -10, 0), centre + new Vector3(10, -10, 0), centre + new Vector3(0, 10, 0)],
            [new NavmeshTriangleFact(0, 1, 2, Preferred: false)],
            [],
            [],
            MapEntry: null);
        return new NavmeshChangedByCleanedMod(
            new RecordKey(TestTargets.TargetName, 0x700),
            TestTargets.Space,
            cell,
            TestTargets.TargetName,
            NavmeshCase.Added,
            new PartOutcome.Unchanged(),
            new PartOutcome.Undo(),
            new PartOutcome.Unchanged(),
            Earlier: null,
            triangle,
            ReplacedBy: [],
            Neighbours: [],
            Doors: [],
            NamedInPreferredPaths: false,
            WinningMapMod: null,
            CellDeletedInAVersion: false,
            LaterWinner: null,
            WinningMapEntry: null);
    }

    public static TargetObject Own(int index, Vector3 position, TestStatic? model = null) =>
        TestTargets.Create(index, TestTargets.At(position), (model ?? Block).Base, TestTargets.Space);

    /// <summary>A rock of the base game that the cleaned mod changed, which goes back to <paramref name="oldPlace"/>.</summary>
    public static RecordChangedByCleanedMod ChangedRock(
        uint id, Vector3 oldPlace, PluginName? winner = null, bool linked = false, TargetId? target = null, PlacedParts undo = PlacedParts.Position) =>
        new(
            new RecordKey(BaseGame, id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            winner ?? TestTargets.TargetName,
            WinningModPastPatches: winner ?? TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f)) with { Undo = undo },
            target,
            linked,
            PresenceBefore: default);

    /// <summary>A cell of the base game whose music the cleaned mod changed and whose location a later plugin changed again.</summary>
    public static CellChangedByCleanedMod ChangedCell(RecordKey cell, (int X, int Y)? grid)
    {
        var water = new CellWaterCopy(BaseGame, HasWater: false, OwnLevel: null);
        return new CellChangedByCleanedMod(
            cell,
            TestTargets.Space,
            grid,
            [
                new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()),
                new PartChange(new CellPartName(CellPart.Location, null), new PartOutcome.LeftToLaterPlugin(Later)),
            ],
            LinkedFromCleanedMod: false,
            TestTargets.TargetName,
            water,
            water,
            DefaultWaterLevel: null);
    }

    public static RemovalDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, params int[] removed) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    public static RestoreWhereRemovedResult Plan(
        ImmutableArray<TargetObject> targets,
        int[] removed,
        IEnumerable<RecordChangedByCleanedMod> records,
        IEnumerable<CellChangedByCleanedMod>? cells = null,
        WhereRemovedRestoreSettings? settings = null,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        int workers = 1,
        IBaseFacts? bases = null,
        VisibleLeftBehindSettings? visibleLeftBehind = null,
        IEnumerable<OtherObject>? others = null,
        CleanedModsObjectRules? cleanedModsObjects = null,
        IEnumerable<NavmeshChangedByCleanedMod>? navmeshes = null,
        IChangedTerrainJudging? changedTerrainRule = null) =>
        Plan(
            targets, DecisionsRemoving(targets, removed), records, cells, settings, spaces, workers, bases: bases, visibleLeftBehind: visibleLeftBehind, others: others,
            cleanedModsObjects: cleanedModsObjects, navmeshes: navmeshes, changedTerrainRule: changedTerrainRule);

    /// <param name="otherModsObjects">Switched off when null.</param>
    public static RestoreWhereRemovedResult Plan(
        ImmutableArray<TargetObject> targets,
        IRunDecisions final,
        IEnumerable<RecordChangedByCleanedMod> records,
        IEnumerable<CellChangedByCleanedMod>? cells = null,
        WhereRemovedRestoreSettings? settings = null,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        int workers = 1,
        OtherModsObjectsResult? otherModsObjects = null,
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = null,
        IBaseFacts? bases = null,
        VisibleLeftBehindSettings? visibleLeftBehind = null,
        IEnumerable<OtherObject>? others = null,
        CleanedModsObjectRules? cleanedModsObjects = null,
        VersionTrust laterTrust = VersionTrust.None,
        IEnumerable<NavmeshChangedByCleanedMod>? navmeshes = null,
        IChangedTerrainJudging? changedTerrainRule = null)
    {
        var collected = TestScenes.CreateWorld(targets, [.. (others ?? []).Select((other, index) => other with { Id = new OtherId(index) })]) with
        {
            ChangedRecords = [.. records],
            ChangedCells = [.. cells ?? []],
            ChangedNavmeshes = [.. navmeshes ?? []],
            Spaces = spaces ?? new Dictionary<RecordKey, SpaceFact>(),
        };
        var rules = GuardRules(workers);
        var caches = CachesOf(targets, bases);
        var restore = settings ?? RestoreSettings;
        return WhereRemovedRestorer.Plan(
            new(
                new(collected, otherModsObjects ?? OtherModsObjectsResult.SwitchedOff),
                final,
                TestClearedAreas.Of(collected, final, caches, restore),
                rules.Over(collected, rules.TooCloseRule(tooClose)),
                TestTargetGroups.None with
                {
                    Accepted = new AcceptedWorld(
                        TestTargets.PatchName,
                        ImmutableDictionary<PluginName, VersionTrust>.Empty.Add(BaseGame, VersionTrust.Full).Add(TestTargets.TargetName, VersionTrust.Full).Add(Later, laterTrust)),
                    LeftBehindNeighbours = [LeftBehindRules.CreateVisibleNeighbours(targets, final, caches, visibleLeftBehind)],
                    CleanedModsObjects = cleanedModsObjects ?? TestTargetGroups.None.CleanedModsObjects,
                    ChangedTerrainRule = changedTerrainRule ?? TestTerrainJudging.FindsNothing,
                },
                new(caches, TestLandPlugin.Flat, Plugins, TestTargets.TargetName),
                new Execution(workers),
                restore,
                () => TimeSpan.Zero));
    }

    public static TestRunCaches CachesOf(ImmutableArray<TargetObject> targets, IBaseFacts? bases = null)
    {
        var objects = TestScenes.Create(targets, [], Shapes);
        return new(
            Shapes, terrain: TestGround.NoTerrain(), bases: bases ?? TestBaseFacts.Unresolved, navmeshes: NoNavmeshes, visibleTargets: objects.IndexVisibleTargets, shownTargets: objects.IndexShownTargets);
    }

    public static TestPlacedGuardRules GuardRules(int workers) =>
        TestPlacedGuardRules.Create(
            Shapes,
            TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []),
            Plugins,
            new OtherModsObjectsSettings(OtherModsObjectsHandling.PullObjectToTheGround, RestoreSettings.TouchGap, SupportLostFraction: 0.5f),
            TestGround.NoTerrain(),
            workers);
}
