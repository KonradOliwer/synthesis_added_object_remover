using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The cleaned mod's objects still standing where an area is restored: removed in the leftovers' round unless a kind the left-behind
/// steps always keep or an object to keep holds them, and the other mods' objects they held up settled again, over in-memory scenes.
/// A small piece or two stand in a clearing a huge removed block cleared: by size nearly nothing stands, and by count the scenes stay
/// under <see cref="FewPiecesMayStand"/>, so the area is cleared by the ordinary rule.
/// </summary>
public sealed class RestoreWhereRemovedLeftoverTests
{
    private const float SearchRadius = 1000f;

    /// <summary>Two of three of the cleaned mod's drawn objects may still stand by number; by size the default 1%.</summary>
    private static readonly ClearedArea FewPiecesMayStand = new(SearchRadius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 70, ClearedAreaShares.Both);

    /// <summary>Any share may stand: for a scene whose many standing pieces only test how the leftovers are removed.</summary>
    private static readonly ClearedArea EveryShareMayStand = new(SearchRadius, MaxStandingSizePercent: 100, MaxStandingCountPercent: 100, ClearedAreaShares.Both);
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
    private static readonly TestStatic Mist = new(new FormKey(ShapeMod, 0x804), @"test\restoreleftovers\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))), EffectOnly: true);
    private static readonly TestStatic Table = new(new FormKey(ShapeMod, 0x805), @"test\restoreleftovers\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, TableHeight))));
    private static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x806), @"test\restoreleftovers\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, LanternHeight))));
    private static readonly TestStatic Floor = new(new FormKey(ShapeMod, 0x807), @"test\restoreleftovers\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-FloorHalfSize, -FloorHalfSize, -10), new Vector3(FloorHalfSize, FloorHalfSize, 0))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "RestoreLeftoverData", HugeBlock, WideBlock, Pebble, Mist, Table, Lantern, Floor));

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
        FewPiecesMayStand,
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
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), Own(2, new Vector3(0, 100, 0), Mist), OwnInvisible(3, new Vector3(-100, 0, 0), XMarker)],
            Removed = [0],
        };

        var (result, decisions) = Plan(scene);

        Assert.Equal([1, 2, 3], result.Leftovers.Select(leftover => leftover.Target.Index));
        Assert.All(result.Leftovers, leftover => Assert.True(leftover.IsRemoved));
        foreach (var index in new[] { 1, 2, 3 }) AssertRemovedAsLeftover(decisions, index);
        Assert.True(Assert.Single(result.Objects).IsRestored);
    }

    /// <summary>
    /// A second rock the cleaned mod moved stands where the huge block is out of its area, so only the pebble stands in its area. The
    /// first rock's area takes the pebble away as a leftover, but the areas were judged before that removal and are not judged again,
    /// so the second rock stays where the cleaned mod put it: removal never spreads from area to area.
    /// </summary>
    [Fact]
    public void ALeftoverRemovedInOneAreaNeverClearsAnotherArea()
    {
        const float PebbleX = 800f;
        const float SecondRockX = 1700f;
        var pebble = new Vector3(PebbleX, 0, 0);
        var secondRock = new Vector3(SecondRockX, 0, 0);
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, pebble, Pebble)],
            Removed = [0],
            Changed = [ChangedRock(0x900, Vector3.Zero), ChangedRock(0x901, secondRock)],
        };

        var (result, decisions) = Plan(scene);

        AssertRemovedAsLeftover(decisions, 1);
        Assert.Equal([1], result.Leftovers.Select(leftover => leftover.Target.Index));
        Assert.Equal([true, false], result.Objects.Select(judged => judged.IsRestored));
        Assert.Equal((1, 1, AreaVerdict.StillStanding), (result.Objects[1].OldPlace!.Objects, result.Objects[1].OldPlace!.Standing, result.Objects[1].OldPlace!.Verdict));
    }

    /// <summary>
    /// A forest of the cleaned mod: trees in a grid, those west of the change removed, those from it eastward standing. The change sits
    /// on the border, so about half of its area still stands by number and by size: at the default limits the area is not cleared,
    /// whether both shares or either must pass, and no standing tree is removed as a leftover.
    /// </summary>
    [Theory]
    [InlineData(ClearedAreaShares.Both)]
    [InlineData(ClearedAreaShares.Either)]
    public void AChangeOnTheBorderOfARemovedClusterAndAStandingForestNeverTrimsTheForest(ClearedAreaShares shares)
    {
        const int Side = 19;
        const float Spacing = 200f;
        const float West = -1800f;
        var places = Enumerable.Range(0, Side * Side).Select(index => new Vector3(West + index % Side * Spacing, West + index / Side * Spacing, 0)).ToList();
        var scene = new Scene
        {
            Targets = [.. places.Select((place, index) => Own(index, place, Table))],
            Removed = [.. places.Index().Where(entry => entry.Item.X < 0).Select(entry => entry.Index)],
            ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5, shares),
        };

        var (result, decisions) = Plan(scene);

        var judged = Assert.Single(result.Objects);
        Assert.Equal((false, AreaVerdict.StillStanding), (judged.IsRestored, judged.OldPlace!.Verdict));
        Assert.True(judged.OldPlace.CountPercent > 40 && judged.OldPlace.SizePercent > 40);
        Assert.Empty(result.Leftovers);
        Assert.All(places.Index().Where(entry => entry.Item.X >= 0), entry => Assert.False(decisions.IsRemoved(new TargetId(entry.Index))));
    }

    /// <summary>
    /// A removed grove of the cleaned mod's trees, 100 units apart, with its standing forest east of it. The change lies so far west
    /// that its area just reaches one tree at the forest's rim: one of about 315 trees, so the area is cleared at the default limits.
    /// That rim tree's own area holds the forest beyond it, so it stays; a pebble standing beside the change, whose own area is the
    /// removed grove, is a true leftover and goes.
    /// </summary>
    [Theory]
    [InlineData(ClearedAreaShares.Both)]
    [InlineData(ClearedAreaShares.Either)]
    public void AClearedAreaThatReachesTheRimOfAStandingForestRemovesOnlyTheTrueLeftovers(ClearedAreaShares shares)
    {
        const float Spacing = 100f;
        const float GroveWest = -1150f;
        const float ForestWest = 950f;
        const float ForestEast = 1950f;
        const float North = 1000f;
        var change = new Vector3(-45, 0, 0);
        var rimTree = new Vector3(ForestWest, 0, 0);
        var places = (from x in Steps(GroveWest, ForestEast, Spacing) from y in Steps(-North, North, Spacing) select new Vector3(x, y, 0)).ToList();
        var pebble = places.Count;
        var scene = new Scene
        {
            Targets = [.. places.Select((place, index) => Own(index, place, Table)), Own(pebble, change + new Vector3(0, 30, 0), Pebble)],
            Removed = [.. places.Index().Where(entry => entry.Item.X < ForestWest).Select(entry => entry.Index)],
            Changed = [ChangedRock(0x900, change)],
            ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5, shares),
        };

        var (result, decisions) = Plan(scene);

        Assert.Equal(AreaVerdict.Cleared, Assert.Single(result.Objects).OldPlace!.Verdict);
        Assert.Equal([pebble], result.Leftovers.Select(leftover => leftover.Target.Index));
        AssertRemovedAsLeftover(decisions, pebble);
        Assert.False(decisions.IsRemoved(new TargetId(places.IndexOf(rimTree))));
        Assert.All(places.Index().Where(entry => entry.Item.X >= ForestWest), entry => Assert.False(decisions.IsRemoved(new TargetId(entry.Index))));
    }

    /// <summary>A marker at the far side of a cleared column: no drawn object of the cleaned mod is in its own area, so nothing it could trim stands, and it goes.</summary>
    [Fact]
    public void AnInvisibleLeftoverWhoseOwnAreaHoldsNoDrawnObjectIsRemoved()
    {
        const float FarSide = -900f;
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), OwnInvisible(1, new Vector3(FarSide, 0, 0), XMarker)],
            Removed = [0],
        };

        var (result, decisions) = Plan(scene);

        Assert.Equal([1], result.Leftovers.Select(leftover => leftover.Target.Index));
        AssertRemovedAsLeftover(decisions, 1);
    }

    private static IEnumerable<float> Steps(float from, float to, float step) =>
        Enumerable.Range(0, (int)MathF.Round((to - from) / step) + 1).Select(index => from + index * step);

    [Fact]
    public void ALeftoverAmongTheObjectsToKeepIsKeptWithItsKeepReason()
    {
        var scene = new Scene
        {
            Targets = [Own(0, HugeBlockPlace, HugeBlock), Own(1, new Vector3(100, 0, 0), Pebble), Own(2, new Vector3(0, 100, 0), Mist)],
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
            ClearedArea = EveryShareMayStand,
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

    /// <summary>A rock of the base game that the cleaned mod moved from the given place, which goes back there.</summary>
    private static RecordChangedByCleanedMod ChangedRock(uint id, Vector3 oldPlace) =>
        new(
            new RecordKey(BaseGame, id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f)),
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    private static ProposedRemoval TooClose(int index) => new(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    /// <returns>The step's result and the run's decisions with the step's rounds added.</returns>
    private static (RestoreWhereRemovedResult Result, IRemovalDecisions Decisions) Plan(Scene scene)
    {
        ImmutableArray<TargetObject> targets = [.. scene.Targets];
        List<OtherObject> others = [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })];
        var collected = TestScenes.CreateWorld(targets, others) with { ChangedRecords = [.. scene.Changed] };
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
                Settings with { ClearedArea = scene.ClearedArea },
                () => TimeSpan.Zero));
        return (decided.Result, decided.Decisions);
    }

    /// <summary>The cleaned mod's objects, which of them a round removed first, and the other mods' objects.</summary>
    private sealed record Scene
    {
        public List<TargetObject> Targets { get; init; } = [];

        public int[] Removed { get; init; } = [];

        public ClearedArea ClearedArea { get; init; } = FewPiecesMayStand;

        /// <summary>The other plugins' records the cleaned mod changed; by default one rock it moved from the origin.</summary>
        public List<RecordChangedByCleanedMod> Changed { get; init; } = [ChangedRock(0x900, Vector3.Zero)];

        public TargetLink[] Links { get; init; } = [];

        /// <summary>Why a target is among the objects to keep, by target index.</summary>
        public IReadOnlyDictionary<int, KeepReason> KeepReasons { get; init; } = new Dictionary<int, KeepReason>();

        public List<OtherObject> Others { get; init; } = [];

        public OtherModsObjectsHandling Handling { get; init; } = OtherModsObjectsHandling.PullObjectToTheGround;

        public IReadOnlySet<InvisibleObjectKind>? InvisibleKinds { get; init; }

        public int Workers { get; init; } = 1;
    }
}
