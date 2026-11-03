using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The cleaned mod's objects still standing where an area is restored: removed in the leftovers' round unless a kind the left-behind
/// steps always keep or an object to keep holds them, and the other mods' objects they held up settled again, over in-memory scenes.
/// A small piece stands in a clearing a huge removed block cleared, so the area is cleared by the ordinary rule.
/// </summary>
public sealed class RestoreWhereRemovedLeftoverTests
{
    private const float SearchRadius = 1000f;
    private const double TenthOfAPercent = 0.1;
    private const float TableHeight = 40f;
    private const float LanternHeight = 6f;
    private const float TouchGap = 1f;
    private const float DropTolerance = 0.01f;
    private const float HugeHalfSize = 300f;
    private const float WideHalfSize = 1000f;
    private const float FloorHalfSize = 2500f;
    private const float BelowTheFloor = 3f;
    private const int Workers = 8;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreLeftoverShapes.esp");
    private static readonly TestStatic HugeBlock = new(new FormKey(ShapeMod, 0x801), @"test\restoreleftovers\hugeblock.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HugeHalfSize), new Vector3(HugeHalfSize))));
    private static readonly TestStatic WideBlock = new(new FormKey(ShapeMod, 0x802), @"test\restoreleftovers\wideblock.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-WideHalfSize), new Vector3(WideHalfSize))));
    private static readonly TestStatic Pebble = new(new FormKey(ShapeMod, 0x803), @"test\restoreleftovers\pebble.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))));
    private static readonly TestStatic Spray = new(new FormKey(ShapeMod, 0x804), @"test\restoreleftovers\spray.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))), EffectOnly: true);
    private static readonly TestStatic Table = new(new FormKey(ShapeMod, 0x805), @"test\restoreleftovers\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, TableHeight))));
    private static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x806), @"test\restoreleftovers\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, LanternHeight))));
    private static readonly TestStatic Floor = new(new FormKey(ShapeMod, 0x807), @"test\restoreleftovers\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-FloorHalfSize, -FloorHalfSize, -10), new Vector3(FloorHalfSize, FloorHalfSize, 0))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "RestoreLeftoverData", HugeBlock, WideBlock, Pebble, Spray, Table, Lantern, Floor));

    private static readonly BaseKey XMarker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [], baseGame: [BaseGame]);


    private static readonly WhereRemovedRestoreSettings Settings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: false,
        Navmeshes: false,
        new ClearedArea(SearchRadius, TenthOfAPercent, ClearedAreaMeasure.ObjectSize),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: 10f, ZoneShape.BoundingBox, NpcHandling.Never),
        new ChangedTerrainSettings(
            On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, 8f, CountTerrainTypeChanges: false, 0,
            CountWaterHeightChanges: false, 0f, CountWaterAddedOrRemoved: false, 0, AreaBySize: false, 0f, AreaByDistance: false, 0f));

    private static readonly INavmeshes NoNavmeshes = TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>>());

    /// <summary>Deep enough that the block, which is not in the columns' way, touches nothing of the floor the other mods' objects stand on.</summary>
    private static readonly Vector3 HugeBlockPlace = new(500, 0, -BelowTheFloor * HugeHalfSize);

    [Fact]
    public void EveryPieceOfTheCleanedModStandingInAClearedAreaIsRemovedAndWhatTheCleanedModChangedGoesBack()
    {
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), Own(2, new Vector3(0, 100, 0), Spray), OwnInvisible(3, new Vector3(-100, 0, 0), XMarker)],
            Removed = [0],
        };

        var (result, decisions) = Plan(scene);

        Assert.Equal([1, 2, 3], result.Leftovers.Select(leftover => leftover.Target.Index));
        Assert.All(result.Leftovers, leftover => Assert.True(leftover.IsRemoved));
        foreach (var index in new[] { 1, 2, 3 }) AssertRemovedAsLeftover(decisions, index);
        Assert.True(Assert.Single(result.Objects).IsRestored);
    }

    [Fact]
    public void ALeftoverAmongTheObjectsToKeepIsKeptWithItsKeepReason()
    {
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), Own(2, new Vector3(0, 100, 0), Spray)],
            Removed = [0],
            KeepReasons = new Dictionary<int, KeepReason> { [1] = TestKeepReasons.Quest },
        };

        var (result, decisions) = Plan(scene);

        var kept = Assert.Single(result.Leftovers, leftover => leftover.Target.Index == 1);
        Assert.Equal(((KeepReason?)TestKeepReasons.Quest, (AlwaysKeptKind?)null, false), (kept.KeptAsObjectToKeep, kept.KeptKind, kept.IsRemoved));
        Assert.False(decisions.IsRemoved(new TargetId(1)));
        AssertRemovedAsLeftover(decisions, 2);
    }

    [Fact]
    public void TheMembersOfARemovedLeftoversLinkedGroupAreRemovedWithIt()
    {
        var farAway = new Vector3(5 * SearchRadius, 0, 0);
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), Own(2, farAway, Pebble)],
            Removed = [0],
            Links = [TestTargets.Link(1, 2)],
        };

        var (result, decisions) = Plan(scene);

        Assert.Equal([1], result.Leftovers.Select(leftover => leftover.Target.Index));
        AssertRemovedAsLeftover(decisions, 1);
        var member = Assert.IsType<Decision.Removed>(decisions.Of(new TargetId(2)));
        Assert.Equal((RoundKind.RestoredArea, new LinkedTo(new TargetId(1))), (member.Round.Kind, member.Reason));
    }

    [Fact]
    public void ALeftoverOfAKindTheLeftBehindSettingsAlwaysKeepIsKept()
    {
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), OwnInvisible(2, new Vector3(-100, 0, 0), XMarker)],
            Removed = [0],
            InvisibleKinds = new HashSet<InvisibleObjectKind> { InvisibleObjectKind.XMarkers },
        };

        var (result, decisions) = Plan(scene);

        var kept = Assert.Single(result.Leftovers, leftover => leftover.Target.Index == 2);
        Assert.Equal(((AlwaysKeptKind?)new AlwaysKeptKind.Invisible(InvisibleObjectKind.XMarkers), (KeepReason?)null, false), (kept.KeptKind, kept.KeptAsObjectToKeep, kept.IsRemoved));
        Assert.False(decisions.IsRemoved(new TargetId(2)));
        AssertRemovedAsLeftover(decisions, 1);
    }

    [Theory]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround, OtherModsObjectFate.PulledDown)]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject, OtherModsObjectFate.Removed)]
    public void AnotherModsObjectRestingOnARemovedLeftoverIsSettledAsTheHandlingSays(OtherModsObjectsHandling handling, OtherModsObjectFate fate)
    {
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, Vector3.Zero, Table)],
            Removed = [0],
            Others = [Placed(0, Floor, Vector3.Zero), Placed(1, Lantern, new Vector3(0, 0, TableHeight))],
            Handling = handling,
        };

        var (result, decisions) = Plan(scene);

        AssertRemovedAsLeftover(decisions, 1);
        var outcome = Assert.Single(result.OtherModsObjects.Objects);
        Assert.Equal(fate, outcome.Fate);
        if (fate == OtherModsObjectFate.PulledDown) Assert.Equal(TableHeight, outcome.Drop, DropTolerance);
    }

    [Fact]
    public void TheResultsDoNotDependOnTheNumberOfWorkers()
    {
        const int PerSide = 5;
        const float Spacing = 100f;
        const float FirstX = 400f;
        const float FirstY = -200f;
        var tables = Enumerable.Range(0, PerSide * PerSide).Select(index => new Vector3(FirstX + index % PerSide * Spacing, FirstY + index / PerSide * Spacing, 0)).ToList();
        var scene = new Scene
        {
            Targets = [Own(0, new Vector3(-700, 0, -BelowTheFloor * WideHalfSize), WideBlock), .. tables.Select((place, index) => Own(1 + index, place, Table))],
            Removed = [0],
            Others = [Placed(0, Floor, Vector3.Zero), .. tables.Select((place, index) => Placed(1 + index, Lantern, place + new Vector3(0, 0, TableHeight)))],
        };

        var one = Plan(scene with { Workers = 1 });
        var many = Plan(scene with { Workers = Workers });

        Assert.Equal(tables.Count, one.Result.Leftovers.Count(leftover => leftover.IsRemoved));
        Assert.Equal(tables.Count, one.Result.OtherModsObjects.Objects.Count(outcome => outcome.Fate == OtherModsObjectFate.PulledDown));
        Assert.Equal(Describe(one, scene.Targets.Count), Describe(many, scene.Targets.Count));
    }

    private static string Describe((RestoreWhereRemovedResult Result, IRemovalDecisions Decisions) planned, int targetCount) => string.Join(
        Environment.NewLine,
        planned.Result.Leftovers.Select(leftover => $"{leftover.Target} {leftover.Column} {leftover.KeptAsObjectToKeep} {leftover.KeptKind}")
            .Concat(planned.Result.OtherModsObjects.Objects.Select(outcome => $"{outcome.Object.Id} {outcome.Fate} {outcome.Drop}"))
            .Concat(planned.Decisions.Rounds.Select(round => $"{round.Number} {round.Kind}"))
            .Concat(Enumerable.Range(0, targetCount).Select(index => $"{index} {planned.Decisions.Of(new TargetId(index))}")));

    private static void AssertRemovedAsLeftover(IRemovalDecisions decisions, int index)
    {
        var removed = Assert.IsType<Decision.Removed>(decisions.Of(new TargetId(index)));
        Assert.Equal(RoundKind.RestoredArea, removed.Round.Kind);
        Assert.IsType<LeftoverInRestoredArea>(removed.Reason);
    }

    private static TargetObject Own(int index, Vector3 position, TestStatic model) =>
        TestTargets.Create(index, TestTargets.At(position), model.Base, TestTargets.Space);

    private static TargetObject OwnInvisible(int index, Vector3 position, BaseKey taggedBase) =>
        TestTargets.Create(index, TestTargets.At(position), taggedBase, TestTargets.Space);

    private static OtherObject Placed(int index, TestStatic model, Vector3 position) => TestShapes.Placed(BaseGameMod, index, model.Base, position);

    /// <summary>A rock of the base game that the cleaned mod moved from the origin, which goes back there.</summary>
    private static RecordChangedByCleanedMod ChangedRock() =>
        new(
            new RecordKey(BaseGame, 0x900),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(Vector3.Zero, Vector3.Zero, 1f)),
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    private static ProposedRemoval TooClose(int index) => new(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    /// <returns>The step's result and the run's decisions with the step's rounds added.</returns>
    private static (RestoreWhereRemovedResult Result, IRemovalDecisions Decisions) Plan(Scene scene)
    {
        ImmutableArray<TargetObject> targets = [.. scene.Targets];
        List<OtherObject> others = [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })];
        var collected = TestScenes.CreateWorld(targets, others) with { ChangedRecords = [ChangedRock()] };
        var support = new OtherModsObjectsSettings(scene.Handling, TouchGap, SupportLostFraction: 0.5f);
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, support, TestGround.NoTerrain(), scene.Workers);
        var decisions = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, scene.Links, TestTargets.References(targets.Length, scene.KeepReasons), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. scene.Removed.Select(TooClose)]);
        var decided = RestoreLoopReuse.PlanDecided(
            new(
                new(collected, OtherModsObjectsResult.SwitchedOff),
                decisions,
                new(rules.WorldCaches, rules.TooCloseRule(), rules.HoldersOfObjectsThatStay, TestNavmeshCheck.NoFault),
                rules.Leftovers(targets, decisions, others, visibleKinds: null, scene.InvisibleKinds),
                new(
                    new TestRunCaches(Shapes, navmeshes: NoNavmeshes, shownTargets: TestScenes.Create(targets, [], Shapes).IndexShownTargets),
                    TestLandPlugin.Flat,
                    Plugins,
                    TestTargets.TargetName),
                new Execution(scene.Workers),
                Settings,
                () => TimeSpan.Zero));
        return (decided.Result, decided.Decisions);
    }

    /// <summary>The cleaned mod's objects, which of them a round removed first, and the other mods' objects.</summary>
    private sealed record Scene
    {
        public List<TargetObject> Targets { get; init; } = [];

        public int[] Removed { get; init; } = [];

        public TargetLink[] Links { get; init; } = [];

        /// <summary>Why a target is among the objects to keep, by target index.</summary>
        public IReadOnlyDictionary<int, KeepReason> KeepReasons { get; init; } = new Dictionary<int, KeepReason>();

        public List<OtherObject> Others { get; init; } = [];

        public OtherModsObjectsHandling Handling { get; init; } = OtherModsObjectsHandling.PullObjectToTheGround;

        public IReadOnlySet<InvisibleObjectKind>? InvisibleKinds { get; init; }

        public int Workers { get; init; } = 1;
    }
}
