using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The scenes the guard tests of the placed records that go back where the cleaned mod was removed share: base game rocks the
/// cleaned mod moved away from a clearing whose only object of the cleaned mod was removed, each checked at the place it goes back to
/// and, for what rests on it, where it stands now.
/// </summary>
internal static class RestoreWhereRemovedGuardScenes
{
    public const float SearchRadius = 1000f;
    public const float TooCloseDistance = 10f;
    public const float RockHalfSize = 20f;
    public const float TouchGap = 1f;

    /// <summary>The default limit by size.</summary>
    public const double MaxStandingSizePercent = 1;

    /// <summary>By number a pebble that stays beside the removed house, one of two, may stand; two of three may not.</summary>
    public const double MaxStandingCountPercent = 60;

    /// <summary>A rock's zone reaches its half size plus the too-close distance from its centre.</summary>
    public const float ZoneReach = RockHalfSize + TooCloseDistance;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreGuardShapes.esp");
    public static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreguards\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    /// <summary>A rock twice as wide as <see cref="Block"/>.</summary>
    public static readonly TestStatic Boulder = new(new FormKey(ShapeMod, 0x80A), @"test\restoreguards\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-2 * RockHalfSize), new Vector3(2 * RockHalfSize))));
    public static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x802), @"test\restoreguards\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));
    /// <summary>27 cubic units, under the share of the removed house's box that may still stand in a cleared area.</summary>
    public static readonly TestStatic Pebble = new(new FormKey(ShapeMod, 0x805), @"test\restoreguards\pebble.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))));

    private static readonly Box RockBounds = new(new Vector3(-RockHalfSize), new Vector3(RockHalfSize));

    /// <summary>An invisible wall: a mesh with collision that stops characters and nothing to render.</summary>
    public static readonly TestStatic CollisionWall = new(new FormKey(ShapeMod, 0x803), @"test\restoreguards\collisionwall.nif", [], NiflySharp.Enums.SkyrimLayer.SKYL_STATIC, RockBounds);

    /// <summary>An invisible marker: a mesh with neither collision nor anything to render.</summary>
    public static readonly TestStatic EmptyMarker = new(new FormKey(ShapeMod, 0x804), @"test\restoreguards\emptymarker.nif", [], Collision: null, RockBounds);

    /// <summary>A base without anything to render whose own box is 4 units wide, for primitive boxes far larger than it.</summary>
    public static readonly TestStatic SmallBase = new(new FormKey(ShapeMod, 0x806), @"test\restoreguards\smallbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)));

    /// <summary>As <see cref="SmallBase"/>, with the engine's marker flag.</summary>
    public static readonly TestStatic MarkerBase = new(new FormKey(ShapeMod, 0x807), @"test\restoreguards\markerbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)), IsMarker: true);

    /// <summary>An activator without a model, such as a trigger box the base game places as a primitive.</summary>
    public static readonly BaseKey TriggerActivator = new(new FormKey(ShapeMod, 0x808).ToRecordKey(), BaseLinkKind.PlaceableObject);

    public static readonly BaseKey AcousticSpaceBase = new(new FormKey(ShapeMod, 0x809).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(
        WithActivatorAndAcousticSpace(TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE))),
        "RestoreGuardData",
        Block, Boulder, Lantern, CollisionWall, EmptyMarker, Pebble, SmallBase, MarkerBase));
    public static readonly BaseKey Marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    public static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    public static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    public static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    public static readonly PluginName Later = LaterMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [], baseGame: [BaseGame]);

    /// <summary>
    /// With rough boxes an object is too close only when its centre lies in the checked object's box grown by the distance, so two
    /// same-size blocks side by side or stacked never are; the scenes are built with that in mind.
    /// </summary>
    public static readonly WhereRemovedRestoreSettings RestoreSettings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: false,
        Navmeshes: false,
        new ClearedArea(SearchRadius, MaxStandingSizePercent, MaxStandingCountPercent),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never));

    private static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap, SupportLostFraction: 0.5f);

    public static readonly Vector3 OldPlace = Vector3.Zero;
    public static readonly Vector3 PlaceNow = new(600, 0, 0);

    /// <summary>Where the cleaned mod put a rock, beyond the area around its old place.</summary>
    public static readonly Vector3 FarPlaceNow = new(3 * SearchRadius, 0, 0);
    public static readonly RecordKey ChangedNavmeshKey = new FormKey(TestTargets.TargetMod, 0x600).ToRecordKey();
    public static readonly RecordKey BaseGameNavmeshKey = new FormKey(BaseGameMod, 0x600).ToRecordKey();

    private static SkyrimMod WithActivatorAndAcousticSpace(SkyrimMod mod)
    {
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(TriggerActivator.Record.ToFormKey(), SkyrimRelease.SkyrimSE));
        mod.AcousticSpaces.Add(new AcousticSpace(AcousticSpaceBase.Record.ToFormKey(), SkyrimRelease.SkyrimSE));
        return mod;
    }

    /// <summary>The objects of one scene; the cleaned mod's objects listed in <see cref="Removed"/> are removed.</summary>
    public sealed record Scene
    {
        public List<TargetObject> Targets { get; init; } = [RemovedHouse()];
        public int[] Removed { get; init; } = [0];

        /// <summary>The targets that are not removed and that no quest protects; every other one that is not removed is protected.</summary>
        public int[] Unprotected { get; init; } = [];

        /// <summary>The removed targets that go back to where they stood before the cleaned mod instead of being disabled, with that place.</summary>
        public Dictionary<int, Vector3> RestoredTo { get; init; } = [];

        /// <summary>The base a restored target had before the cleaned mod replaced it; the target's own base when not listed.</summary>
        public Dictionary<int, BaseKey> EarlierBases { get; init; } = [];

        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The other plugins' objects that stand now, numbered in this order.</summary>
        public List<OtherObject> Others { get; init; } = [];

        /// <summary>The objects that stand now that the removal steps read as support only, numbered after <see cref="Others"/>.</summary>
        public List<OtherObject> SupportOnly { get; init; } = [];

        public List<NavmeshWorldTriangle> Navmesh { get; init; } = [];
        public List<RecordKey> ChangedNavmeshes { get; init; } = [];

        /// <summary>What became of other mods' objects left without support; their ids are their places in <see cref="Others"/>.</summary>
        public OtherModsObjectsResult OtherMods { get; init; } = OtherModsObjectsResult.SwitchedOff;

        /// <summary>The too-close settings of the collision group that judges the targets that are not removed; none judges them when null.</summary>
        public TooCloseSettings? StandingTooClose { get; init; }

        /// <summary>How far the removal steps believe Later.esp.</summary>
        public VersionTrust LaterTrust { get; init; } = VersionTrust.None;

        /// <summary>The removal steps' follow-up settings.</summary>
        public AlsoRemoveSettings FollowUp { get; init; } = TestPlacedGuardRules.DefaultFollowUp;

        /// <summary>The zone shape of the restore's too-close settings.</summary>
        public ZoneShape Zone { get; init; } = RestoreSettings.TooClose.Zone;

        /// <summary>The distance of the restore's too-close settings.</summary>
        public float TooCloseDistance { get; init; } = RestoreSettings.TooClose.DistanceInUnits;

        public int Workers { get; init; } = 1;
    }

    /// <summary>The cleaned mod's only visible object near the old places, which the rounds removed.</summary>
    public static TargetObject RemovedHouse() => Own(0, new Vector3(200, 300, 0));

    /// <summary>The cleaned mod's second object: a base game crate it moved, which a removal step puts back.</summary>
    public static TargetObject MovedCrate() => Own(1, new Vector3(-300, 300, 0)) with { Origin = TargetOrigin.Moved };

    /// <summary>
    /// The house and <see cref="MovedCrate"/> are removed, and the crate goes back to <paramref name="distance"/> east of the rock's old
    /// place, where the rock goes back to; the rock stands at <see cref="PlaceNow"/> meanwhile.
    /// </summary>
    public static Scene RestoredCrateBesideRock(RecordChangedByCleanedMod rock, float distance = ZoneReach - 5f) =>
        new()
        {
            Targets = [RemovedHouse(), MovedCrate()],
            Removed = [0, 1],
            RestoredTo = new Dictionary<int, Vector3> { [1] = OldPlace + new Vector3(distance, 0, 0) },
            Rocks = [rock],
            Others = [RockNow(0, PlaceNow)],
        };

    public static TargetObject Own(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space);

    public static TargetObject PebbleAt(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), Pebble.Base, TestTargets.Space);

    public static RecordKey RockKey(int index) => new FormKey(BaseGameMod, 0x900 + (uint)index).ToRecordKey();

    /// <summary>A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/>, whose position goes back.</summary>
    /// <param name="model">The rock's base; a block when null.</param>
    /// <param name="reference">What its winning version says about the reference itself; a plain reference when null.</param>
    public static RecordChangedByCleanedMod Rock(int index, Vector3 oldPlace, Vector3 now, BaseKey? model = null, ReferenceFacts? reference = null) =>
        new(
            RockKey(index),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            model ?? Block.Base,
            reference ?? TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f), model ?? Block.Base) with { Undo = PlacedParts.Position },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    /// <summary>The rock where the cleaned mod put it: its override is a change in the world without the cleaned mod.</summary>
    /// <param name="model">The rock's base; a block when null.</param>
    public static OtherObject RockNow(int index, Vector3 now, BaseKey? model = null) =>
        TestShapes.Placed(BaseGameMod, index, model ?? Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };

    public static OtherObject LaterObject(int index, BaseKey model, Vector3 position) =>
        TestShapes.Placed(LaterMod, index, model, position) with { ChangeWithoutCleanedMod = new ObjectChange(Later, AcceptedFrom: null) };

    public static MeshTriangle FlatTriangle(Vector3 centre) =>
        new(centre + new Vector3(-100, -100, 0), centre + new Vector3(100, -100, 0), centre + new Vector3(0, 100, 0));

    private static NavmeshChangedByCleanedMod ChangedNavmesh(RecordKey navmesh)
    {
        var cell = new FormKey(TestTargets.TargetMod, 0x601).ToRecordKey();
        return new NavmeshChangedByCleanedMod(
            navmesh,
            TestTargets.Space,
            cell,
            TestTargets.TargetName,
            NavmeshCase.Added,
            new PartOutcome.Unchanged(),
            new PartOutcome.Undo(),
            new PartOutcome.Unchanged(),
            Earlier: null,
            new NavmeshShape(TestTargets.TargetName, cell, IsDeleted: false, [], [], [], [], MapEntry: null),
            ReplacedBy: [],
            Neighbours: [],
            Doors: [],
            NamedInPreferredPaths: false,
            WinningMapMod: null,
            CellDeletedInAVersion: false,
            LaterWinner: null,
            WinningMapEntry: null);
    }

    public static RestoreWhereRemovedResult Plan(Scene scene)
    {
        ImmutableArray<TargetObject> targets = [.. scene.Targets];
        List<OtherObject> others = [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })];
        List<OtherObject> supportOnly = [.. scene.SupportOnly.Select((other, index) => other with { Id = new OtherId(others.Count + index), Change = null })];
        var collected = TestScenes.CreateWorld(targets, others, supportOnly) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedNavmeshes = [.. scene.ChangedNavmeshes.Select(ChangedNavmesh)],
        };
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), scene.Workers);
        ImmutableArray<NavmeshWorldTriangle> navmesh = [.. scene.Navmesh];
        var decisions = DecisionsRemoving(targets, scene.Removed, scene.RestoredTo, scene.EarlierBases, scene.Unprotected);
        var caches = new TestRunCaches(
            Shapes,
            terrain: TestGround.NoTerrain(),
            bases: TestBaseFacts.Unresolved,
            navmeshes: new NavmeshIndex(bucket => bucket.Grid is null && !navmesh.IsEmpty ? [.. navmesh] : null),
            shownTargets: TestScenes.Create(targets, [], Shapes).IndexShownTargets);
        return WhereRemovedRestorer.Plan(
            new(
                new(collected, scene.OtherMods),
                decisions,
                TestClearedAreas.Of(collected, decisions, caches, RestoreSettings),
                rules.Over(collected, alsoRemove: scene.FollowUp),
                new(
                    TestTargetGroups.None.ChangedTerrain,
                    scene.StandingTooClose is { } standingTooClose
                        ? TestTargetGroups.SettingsOf(
                            Enumerable.Range(0, targets.Length).Except(scene.Removed).Select(index => new TargetId(index)), standingTooClose)
                        : TestTargetGroups.None.TooClose,
                    new AcceptedWorld(
                        TestTargets.PatchName,
                        ImmutableDictionary<PluginName, VersionTrust>.Empty.Add(BaseGame, VersionTrust.Full).Add(TestTargets.TargetName, VersionTrust.Full).Add(Later, scene.LaterTrust)),
                    LeftBehindNeighbours: [],
                    TestTargetGroups.None.CleanedModsObjects,
                    TestTerrainJudging.FindsNothing),
                new(
                    caches,
                    TestLandPlugin.Flat,
                    Plugins,
                    TestTargets.TargetName),
                new Execution(scene.Workers),
                RestoreSettings with { TooClose = RestoreSettings.TooClose with { Zone = scene.Zone, DistanceInUnits = scene.TooCloseDistance } },
                () => TimeSpan.Zero));
    }

    /// <param name="restoredTo">The removed targets that go back to the given place instead of being disabled.</param>
    /// <remarks>Every target that is not removed is an object to keep (a record of the cleaned mod links to it).</remarks>
    /// <param name="unprotected">The targets that are not removed and that a quest does not protect.</param>
    private static RemovalDecisions DecisionsRemoving(
        ImmutableArray<TargetObject> targets, int[] removed, IReadOnlyDictionary<int, Vector3> restoredTo, IReadOnlyDictionary<int, BaseKey> earlierBases, int[]? unprotected = null) =>
        RemovalDecisions.Start(
                ObjectsToKeep.Build(
                    targets,
                    [],
                    TestTargets.References(targets.Length, Enumerable.Range(0, targets.Length).Except(removed).Except(unprotected ?? []).ToDictionary(index => index, _ => TestKeepReasons.Quest)),
                    TestBaseFacts.Unresolved),
                [.. targets.Select((target, index) => WhenRemovedOf(restoredTo.TryGetValue(index, out var place) ? place : (Vector3?)null, earlierBases.TryGetValue(index, out var earlierBase) ? earlierBase : target.Base))])
            .Apply(RoundKind.TooClose, [.. removed.Select(index => new ProposedRemoval(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);

    private static WhenRemoved WhenRemovedOf(Vector3? restoredTo, BaseKey? earlierBase) =>
        restoredTo is { } place
            ? new WhenRemoved.Restore(TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(place, Vector3.Zero, 1f), earlierBase))
            : new WhenRemoved.Disable();
}
