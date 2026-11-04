using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The area rule of restoring where the cleaned mod was removed: the cleaned mod's own visible objects in a column around a judged
/// point, their fates, the share left standing and the never-rules, over in-memory target objects.
/// </summary>
public sealed class RestoreWhereRemovedTests
{
    private const float SearchRadius = 1000f;
    private const float OutsideTheArea = 1500f;
    private const double TenthOfAPercent = 0.1;
    private const double BlockVolume = 40d * 40 * 40;
    private const double BigBlockVolume = 200d * 200 * 200;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreAreaShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restorearea\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    private static readonly TestStatic BigBlock = new(new FormKey(ShapeMod, 0x802), @"test\restorearea\bigblock.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100), new Vector3(100))));
    private static readonly TestStatic Flat = new(new FormKey(ShapeMod, 0x803), @"test\restorearea\flat.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 0))));
    /// <summary>A puff of fog: a mesh whose only shape is drawn with an effect shader, as big as <see cref="Block"/>.</summary>
    private static readonly TestStatic Mist = new(new FormKey(ShapeMod, 0x804), @"test\restorearea\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))), EffectOnly: true);

    /// <summary>A fog volume drawn only with an effect shader, far larger than every solid piece here.</summary>
    private static readonly TestStatic Fog = new(new FormKey(ShapeMod, 0x805), @"test\restorearea\fog.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-800), new Vector3(800))), EffectOnly: true);
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "RestoreWhereRemovedData", Block, BigBlock, Flat, Mist, Fog));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);

    private static readonly WhereRemovedRestoreSettings Settings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: true,
        Navmeshes: false,
        new ClearedArea(SearchRadius, MaxStandingSizePercent: TenthOfAPercent, MaxStandingCountPercent: TenthOfAPercent),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: 10f, ZoneShape.BoundingBox, NpcHandling.Never));

    private static readonly WhereRemovedRestoreSettings HalfMayStand =
        Settings with { ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 50, MaxStandingCountPercent: 50) };

    /// <summary>The default limits, 1% by size and 5% by number.</summary>
    private static readonly WhereRemovedRestoreSettings AtTheDefaults =
        Settings with { ClearedArea = new ClearedArea(SearchRadius, MaxStandingSizePercent: 1, MaxStandingCountPercent: 5) };

    private static readonly RecordKey ExteriorCellKey = new(BaseGame, 0x500);

    private static readonly INavmeshes NoNavmeshes = TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>>());

    private static readonly GuardReach Reach = new(Shapes, GuardRules(workers: 1).TooCloseToWhatStays, Settings.TooClose, touchGap: 1f);

    [Fact]
    public void AChangeWhoseAreaIsClearedOfTheCleanedModsObjectsGoesBack()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0, 1, 2], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 0, AreaVerdict.Cleared), (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Verdict));
        Assert.Equal(3 * BlockVolume, judged.OldPlace.Size);
        Assert.True(judged.IsRestored);
    }

    [Fact]
    public void AChangeWhereOneRemovedPieceStandsAmongKeptOnesStays()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0))];

        var judged = Assert.Single(Plan(targets, removed: [1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 2, AreaVerdict.StillStanding), (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    [Fact]
    public void ALoneRemovedPieceLeavesNothingStandingSoItsChangeGoesBack()
    {
        var judged = Assert.Single(Plan([Own(0, new Vector3(300, 0, 0))], removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, 0, AreaVerdict.Cleared), (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Verdict));
        Assert.True(judged.IsRestored);
    }

    /// <summary>The cleaned mod's only object stands outside the area, and another plugin's object it moved stands inside, which does not count.</summary>
    [Fact]
    public void AChangeWithNoObjectOfTheCleanedModAroundStays()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(OutsideTheArea, 0, 0)), Own(1, new Vector3(50, 0, 0)) with { Origin = TargetOrigin.Moved }];

        var judged = Assert.Single(Plan(targets, removed: [0, 1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((0, AreaVerdict.NoObjectsAround), (judged.OldPlace!.Objects, judged.OldPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    [Fact]
    public void TheAreaIsAColumnSoAnObjectFarAboveTheOldPlaceCounts()
    {
        var judged = Assert.Single(Plan([Own(0, new Vector3(0, 0, 20000))], removed: [], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, AreaVerdict.StillStanding), (judged.OldPlace!.Objects, judged.OldPlace.Verdict));
    }

    /// <summary>One object of the area stands; at a tenth of a percent, 1 in 1000 is just allowed and 1 in 999 is not.</summary>
    [Theory]
    [InlineData(1000, AreaVerdict.Cleared)]
    [InlineData(999, AreaVerdict.StillStanding)]
    public void OneStandingObjectInAThousandIsTheMostATenthOfAPercentAllows(int objects, AreaVerdict expected)
    {
        const int Side = 32;
        const float Spacing = 20f;
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, objects).Select(index => Own(index, new Vector3(index % Side * Spacing, index / Side * Spacing, 0)))];

        var judged = Assert.Single(Plan(targets, removed: [.. Enumerable.Range(1, objects - 1)], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((objects, 1, expected), (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Verdict));
        Assert.Equal(judged.OldPlace.CountPercent, judged.OldPlace.SizePercent, precision: 9);
    }

    /// <summary>A big object stands among three small removed ones: a quarter of them by count, nearly all by size.</summary>
    [Fact]
    public void OneBigObjectStandingAmongSmallRemovedOnesKeepsItsAreaBySize()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0)),
        ];

        var judged = Assert.Single(Plan(targets, removed: [1, 2, 3], [ChangedRock(0x900, Vector3.Zero)], settings: HalfMayStand).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 25d), (judged.OldPlace!.Verdict, judged.OldPlace.CountPercent));
        Assert.Equal((BigBlockVolume, BigBlockVolume + 3 * BlockVolume), (judged.OldPlace.StandingSize, judged.OldPlace.Size));
    }

    /// <summary>A big object was removed among three small ones that stand: nearly nothing by size, three quarters by count.</summary>
    [Fact]
    public void SmallObjectsStandingBesideOneBigRemovedOneKeepTheirAreaByCount()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0)),
        ];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: HalfMayStand).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 75d), (judged.OldPlace!.Verdict, judged.OldPlace.CountPercent));
        Assert.True(judged.OldPlace.SizePercent < 50);
    }

    /// <summary>A big object was removed beside a small one that stands: under 1% by size but half by number, so the area is not cleared.</summary>
    [Fact]
    public void OneBigRemovedObjectBesideASmallStandingOneDoesNotClearTheArea()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: AtTheDefaults).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 50d), (judged.OldPlace!.Verdict, judged.OldPlace.CountPercent));
        Assert.True(judged.OldPlace.SizePercent < 1);
    }

    /// <summary>Effect objects have no size, so their number alone decides: two of three still stand.</summary>
    [Fact]
    public void AnAreaOfOnlyEffectObjectsIsJudgedByNumberAlone()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), Fog), Own(1, new Vector3(-100, 0, 0), Fog), Own(2, new Vector3(0, 100, 0), Fog)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: AtTheDefaults).Objects);

        Assert.Equal((0d, AreaVerdict.StillStanding), (judged.OldPlace!.Size, judged.OldPlace.Verdict));
    }

    /// <summary>Objects without a volume leave no size to weigh, so their count alone decides.</summary>
    [Fact]
    public void AnAreaWhoseObjectsHaveNoVolumeIsJudgedByCount()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(0, 100, 0), Flat), Own(1, new Vector3(0, -100, 0), Flat)];

        var judged = Assert.Single(Plan(targets, removed: [1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((2, 0d, 50d, 0d, AreaVerdict.StillStanding), (judged.OldPlace!.Objects, judged.OldPlace.Size, judged.OldPlace.CountPercent, judged.OldPlace.SizePercent, judged.OldPlace.Verdict));
    }

    /// <summary>
    /// A fog volume of the cleaned mod far larger than its other pieces was removed; three small pieces stand. The fog counts once and
    /// adds no size, so the standing pieces keep the area by count and by size.
    /// </summary>
    [Fact]
    public void ALargeRemovedEffectVolumeCountsOnceAndAddsNoSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero, Fog), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((4, 3 * BlockVolume, 75d, 100d, AreaVerdict.StillStanding), (judged.OldPlace!.Objects, judged.OldPlace.Size, judged.OldPlace.CountPercent, judged.OldPlace.SizePercent, judged.OldPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    /// <summary>The cleaned mod is made only of effect objects, so the area has no size and is judged by count alone.</summary>
    [Theory]
    [InlineData(new[] { 0, 1, 2 }, AreaVerdict.Cleared)]
    [InlineData(new[] { 0, 1 }, AreaVerdict.StillStanding)]
    public void AnAreaOfOnlyEffectObjectsIsJudgedByCount(int[] removed, AreaVerdict expected)
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), Fog), Own(1, new Vector3(-100, 0, 0), Fog), Own(2, new Vector3(0, 100, 0), Fog)];

        var judged = Assert.Single(Plan(targets, removed, [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 0d, expected), (judged.OldPlace!.Objects, judged.OldPlace.Size, judged.OldPlace.Verdict));
    }

    [Fact]
    public void AnInteriorIsOneAreaSoAnObjectFarFromTheOldPlaceCounts()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(20000, 0, 0))];
        var interior = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, null, SpaceKind.Interior, null) };

        var inInterior = Assert.Single(Plan(targets, removed: [], [ChangedRock(0x900, Vector3.Zero)], spaces: interior).Objects);
        var inWorldspace = Assert.Single(Plan(targets, removed: [], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((AreaVerdict.StillStanding, AreaVerdict.NoObjectsAround), (inInterior.OldPlace!.Verdict, inWorldspace.OldPlace!.Verdict));
    }

    /// <summary>Another plugin's object the cleaned mod moved from a cleared place into a village that stays.</summary>
    [Theory]
    [InlineData(false, AreaVerdict.StillStanding, false)]
    [InlineData(true, AreaVerdict.Cleared, true)]
    public void AnUndecidedMovedTargetIsJudgedAtItsOldPlaceAndWhereItStands(bool villageRemoved, AreaVerdict whereItIsNow, bool restored)
    {
        var village = new Vector3(5000, 0, 0);
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(100, 0, 0)),
            Own(1, village + new Vector3(100, 0, 0)),
            Own(2, village) with { Origin = TargetOrigin.Moved },
        ];

        var judged = Assert.Single(Plan(targets, removed: villageRemoved ? [0, 1] : [0], [ChangedRock(0x900, Vector3.Zero, target: new TargetId(2))]).Objects);

        Assert.Equal((AreaVerdict.Cleared, whereItIsNow, restored), (judged.OldPlace!.Verdict, judged.WhereItIsNow!.Verdict, judged.IsRestored));
    }

    [Fact]
    public void ATargetTheRemovalStepsDecidedOrWhoseMarkerTheyMovedIsNotJudged()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(100, 0, 0)),
            Own(1, new Vector3(200, 0, 0)) with { Origin = TargetOrigin.Moved },
            Own(2, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved },
        ];
        var final = DecisionsRemoving(targets, 0, 1).AddMoves([new MarkerMove(new TargetId(2), new Vector3(300, 0, 0), new Vector3(310, 0, 0))]);

        var result = Plan(
            targets, final, [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1)), ChangedRock(0x901, Vector3.Zero, target: new TargetId(2))]);

        Assert.Empty(result.Objects);
    }

    /// <summary>A marker move took a target the cleaned mod moved 10 units on; the after-removal world has it standing there now.</summary>
    [Fact]
    public void ACandidateThatIsATargetStandsNowWhereTheRunsMovesLeaveIt()
    {
        var movedTo = new Vector3(310, 0, 0);
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved }];
        var final = DecisionsRemoving(targets, 0).AddMoves([new MarkerMove(new TargetId(1), new Vector3(300, 0, 0), movedTo)]);

        var change = ChangedRock(0x900, Vector3.Zero, target: new TargetId(1)) with { Key = targets[1].Key };

        var world = AfterRemovalWorld.Of(TestScenes.CreateWorld(targets, []), final, OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, [change], Reach);

        Assert.Equal(movedTo, Assert.Single(world.Now).Transform.Position);
    }

    /// <summary>
    /// Two rocks the cleaned mod changed stand now: one is a change in the world without the cleaned mod, one is not (a later
    /// plugin carries the cleaned mod's value), so its copy is a support-only object and causes no removals.
    /// </summary>
    [Fact]
    public void OnlyTheCopiesOfCandidatesThatAreAChangeWithoutTheCleanedModAreCausesTheTooCloseRuleLeavesOut()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0))];
        var changed = TestShapes.Placed(BaseGameMod, 0, Block.Base, new Vector3(500, 0, 0)) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };
        var carried = TestShapes.Placed(BaseGameMod, 1, Block.Base, new Vector3(900, 0, 0));
        var collected = TestScenes.CreateWorld(targets, [changed], [carried]);

        var world = AfterRemovalWorld.Of(
            collected, DecisionsRemoving(targets, 0), OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, [ChangedRock(0x900, Vector3.Zero), ChangedRock(0x901, Vector3.Zero)], Reach);
        var both = new HashSet<int> { 0, 1 };

        Assert.Equal(2, world.CopiesNowOf(both).Count());
        var cause = Assert.Single(world.CauseCopiesNowOf(both));
        Assert.Equal(changed.Key, world.World.Other(cause).Key);
    }

    /// <summary>The cleaned mod moved a vanilla rock into its camp, and a later mod's lantern rests on the rock there, so the rock is kept.</summary>
    [Fact]
    public void AKeptTargetIsNotJudgedSoWhatRestsOnItWhereItStandsIsNotLeftFloating()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(300, 0, 0)) with { Origin = TargetOrigin.Moved }];
        var resting = new Dictionary<int, KeepReason> { [1] = new(KeepKind.OtherModsObjectRestsOnIt) };
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, resting), TestBaseFacts.Unresolved);
        var final = TestSeededDecisions.StartDisablingAll(protection, targets.Length).Apply(RoundKind.TooClose, [TooClose(0)]);

        var result = Plan(targets, final, [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1))]);

        Assert.Empty(result.Objects);
    }

    /// <summary>The other mods' objects step pulled one changed rock down and removed another.</summary>
    [Fact]
    public void ARecordTheOtherModsObjectsStepTookAwayIsNotJudged()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0))];
        var otherModsObjects = OtherModsObjectsResult.SwitchedOff with
        {
            Outcome = StepOutcome.Performed,
            Objects =
            [
                new OtherModsObjectOutcome(TestShapes.Placed(BaseGameMod, 0, Block.Base, Vector3.Zero), OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 10f, ReferenceRoles.None),
                new OtherModsObjectOutcome(TestShapes.Placed(BaseGameMod, 1, Block.Base, Vector3.Zero), OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None),
            ],
        };

        var result = Plan(
            targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero), ChangedRock(0x901, Vector3.Zero), ChangedRock(0x902, Vector3.Zero)], otherModsObjects: otherModsObjects);

        Assert.Equal([new RecordKey(BaseGame, 0x902)], result.Objects.Select(judged => judged.Change.Key));
    }

    /// <summary>A kept marker of the cleaned mod stands next to its only visible object, which was removed.</summary>
    [Fact]
    public void AnInvisibleObjectOfTheCleanedModDoesNotCount()
    {
        var marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), TestTargets.Create(1, TestTargets.At(new Vector3(200, 0, 0)), marker, TestTargets.Space)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, 0, AreaVerdict.Cleared), (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Verdict));
    }

    /// <summary>The cleaned mod's mist, whose mesh holds only effect shapes, still stands next to its removed block.</summary>
    [Fact]
    public void AStandingObjectWhoseMeshHoldsOnlyEffectsKeepsItsAreaStillStandingByCountAndAddsNoSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(-100, 0, 0), Mist)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal(
            (2, 1, BlockVolume, 0d, AreaVerdict.StillStanding),
            (judged.OldPlace!.Objects, judged.OldPlace.Standing, judged.OldPlace.Size, judged.OldPlace.StandingSize, judged.OldPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    /// <summary>The big block was kept shrunk to half its size, so it counts with an eighth of its volume.</summary>
    [Fact]
    public void AnObjectKeptShrunkCountsAtItsShrunkBox()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), BigBlock), Own(1, new Vector3(-100, 0, 0))];
        var shrunk = DecisionsRemoving(targets, 1).ApplyFits([new ProposedFit(targets[0].Id, TooClose(0).Reason, targets[0].Placement with { Scale = 0.5f })]);

        var judged = Assert.Single(Plan(targets, shrunk, [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, BigBlockVolume / 8), (judged.OldPlace!.Standing, judged.OldPlace.StandingSize));
    }

    [Fact]
    public void TheCandidatesAreJudgedInKeyOrderWhateverOrderTheyComeIn()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(3000, 0, 0))];
        RecordChangedByCleanedMod[] rocks = [.. Enumerable.Range(0, 6).Select(index => ChangedRock(0x900 + (uint)index, new Vector3(index * 600, 0, 0)))];
        CellChangedByCleanedMod[] cells = [.. Enumerable.Range(0, 4).Select(index => ChangedCell(new RecordKey(BaseGame, 0x500 + (uint)index), (index % 2 - 1, index / 2 - 1)))];

        var inOrder = Plan(targets, removed: [0], rocks, cells);
        var reversed = Plan(targets, removed: [0], Enumerable.Reverse(rocks), Enumerable.Reverse(cells));

        Assert.Equal(inOrder.Objects.Select(Describe), reversed.Objects.Select(Describe));
        Assert.Equal(inOrder.Cells.Select(Describe), reversed.Cells.Select(Describe));
        Assert.Equal(rocks.Select(rock => rock.Key), inOrder.Objects.Select(judged => judged.Change.Key));
    }

    [Fact]
    public void ARecordWithNothingThatGoesBackIsNotJudged()
    {
        var leftToLater = ChangedRock(0x900, Vector3.Zero, undo: PlacedParts.None);

        Assert.Empty(Plan([Own(0, new Vector3(100, 0, 0))], removed: [0], [leftToLater]).Objects);
    }

    [Theory]
    [InlineData(true, false, NeverRestoredReason.WinnerLoadsAfterPatch)]
    [InlineData(true, true, NeverRestoredReason.WinnerLoadsAfterPatch)]
    [InlineData(false, true, NeverRestoredReason.LinkedFromCleanedMod)]
    public void AChangeANeverRuleHoldsForStaysInACleanedArea(bool winnerLoadsAfterPatch, bool linkedFromCleanedMod, NeverRestoredReason expected)
    {
        var rock = ChangedRock(0x900, Vector3.Zero, winner: winnerLoadsAfterPatch ? Later : TestTargets.TargetName, linked: linkedFromCleanedMod);

        var judged = Assert.Single(Plan([Own(0, new Vector3(100, 0, 0))], removed: [0], [rock]).Objects);

        Assert.Equal(((NeverRestoredReason?)expected, (AreaShare?)null, false), (judged.Never, judged.OldPlace, judged.IsRestored));
    }

    [Fact]
    public void AnExteriorCellIsJudgedAtItsCentreAndOnlyItsUndonePartsAreListed()
    {
        var centre = new Vector3(ExteriorGrid.CellSize / 2, ExteriorGrid.CellSize / 2, 0);
        ImmutableArray<TargetObject> targets = [Own(0, centre), Own(1, Vector3.Zero)];
        var cell = ChangedCell(ExteriorCellKey, (0, 0));

        var judged = Assert.Single(Plan(targets, removed: [0], [], [cell]).Cells);

        Assert.Equal((1, AreaVerdict.Cleared, true), (judged.Area!.Objects, judged.Area.Verdict, judged.IsRestored));
        Assert.Equal([new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo())], judged.Undone.ToList());
    }

    [Fact]
    public void AnInteriorCellIsJudgedOverTheWholeInterior()
    {
        var interior = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, null, SpaceKind.Interior, null) };
        var cell = ChangedCell(TestTargets.Space, grid: null);

        var judged = Assert.Single(Plan([Own(0, new Vector3(20000, 0, 0))], removed: [], [], [cell], spaces: interior).Cells);

        Assert.Equal((1, AreaVerdict.StillStanding), (judged.Area!.Objects, judged.Area.Verdict));
    }

    [Fact]
    public void ACellALaterPluginWinsOrTheCleanedModLinksToStays()
    {
        var later = ChangedCell(ExteriorCellKey, (0, 0)) with { WinningMod = Later };
        var linked = ChangedCell(new RecordKey(BaseGame, 0x501), (1, 0)) with { LinkedFromCleanedMod = true };

        var result = Plan([Own(0, Vector3.Zero)], removed: [0], [], [later, linked]);

        Assert.Equal(
            [(NeverRestoredReason.WinnerLoadsAfterPatch, false), (NeverRestoredReason.LinkedFromCleanedMod, false)],
            result.Cells.Select(judged => (judged.Never!.Value, judged.IsRestored)));
    }

    [Fact]
    public void AnUnexpectedErrorStopsTheStepWithItsFailureAndJudgesNothing()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0))];

        var result = Plan(targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero)], worldCaches: new TestObjectCachesOfWorlds(_ => throw new PlantedUserDataException("broke")));

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Empty(result.Objects);
        Assert.Empty(result.Cells);
        Assert.Empty(result.NavmeshGroups);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((RestoreWhereRemovedParts.Step, "PlantedUserDataException: broke", (FailureSubject?)null), (failure.Part, failure.Failure, failure.Subject));
        Assert.NotNull(failure.Details);
    }

    /// <summary>The record whose check stopped stays as the cleaned mod changed it, and the failure names it.</summary>
    [Fact]
    public void ARecordWhoseCheckStoppedUnexpectedlyStaysAndIsNamedByAnObjectCheckFailure()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0))];

        var result = Plan(targets, DecisionsRemoving(targets, 0), [ChangedRock(0x900, Vector3.Zero)], tooClose: (_, _, _, _) => new StoppingRule());

        var judged = Assert.Single(result.Objects);
        Assert.False(judged.IsRestored);
        Assert.Equal("InvalidOperationException: broke", judged.Refused!.Failure);
        var failure = Assert.Single(result.Failures);
        Assert.Equal((RestoreWhereRemovedParts.ObjectCheck, "InvalidOperationException: broke"), (failure.Part, failure.Failure));
        Assert.Equal((judged.Change.Key.ToString(), 0), (failure.Subject!.Key, failure.Subject.Position));
        Assert.Equal(StepOutcome.Performed, result.Outcome);
    }

    [Fact]
    public void EachKindIsJudgedOnlyWithItsSwitchOn()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero)];
        RecordChangedByCleanedMod[] rocks = [ChangedRock(0x900, Vector3.Zero)];
        CellChangedByCleanedMod[] cells = [ChangedCell(ExteriorCellKey, (0, 0))];

        var rules = GuardRules(workers: 1);
        var decisions = DecisionsRemoving(targets);
        var world = TestScenes.CreateWorld(targets, []);
        var caches = CachesOf(targets);
        var allOff = Settings with { OtherModsObjects = false, Terrain = false, Cells = false, Navmeshes = false };
        var groupOff = WhereRemovedRestorer.Plan(
            new(
                new(world, OtherModsObjectsResult.SwitchedOff),
                decisions,
                TestClearedAreas.Of(world, decisions, caches, allOff),
                new(rules.WorldCaches, rules.TooCloseRule(), rules.HoldersOfObjectsThatStay, TestNavmeshCheck.NoFault),
                TestTargetGroups.None,
                new(caches, TestLandPlugin.Flat, Plugins, TestTargets.TargetName),
                new Execution(1),
                allOff,
                () => TimeSpan.Zero));
        var objectsOff = Plan(targets, removed: [0], rocks, cells, Settings with { OtherModsObjects = false });
        var cellsOff = Plan(targets, removed: [0], rocks, cells, Settings with { Cells = false });

        Assert.Equal(StepOutcome.SwitchedOff, groupOff.Outcome);
        Assert.Equal((0, 1), (objectsOff.Objects.Length, objectsOff.Cells.Length));
        Assert.Equal((1, 0), (cellsOff.Objects.Length, cellsOff.Cells.Length));
    }

    [Fact]
    public void TheResultsDoNotDependOnTheNumberOfWorkers()
    {
        const int ObjectCount = 400;
        const int RockCount = 200;
        var random = new Random(8);
        ImmutableArray<TargetObject> targets =
        [
            .. Enumerable.Range(0, ObjectCount).Select(index => Own(index, RandomPoint(random), random.Next(5) == 0 ? BigBlock : Block)),
        ];
        int[] removed = [.. Enumerable.Range(0, ObjectCount).Where(_ => random.Next(10) != 0)];
        RecordChangedByCleanedMod[] rocks = [.. Enumerable.Range(0, RockCount).Select(index => ChangedRock(0x900 + (uint)index, RandomPoint(random)))];
        CellChangedByCleanedMod[] cells = [.. Enumerable.Range(0, 4).Select(index => ChangedCell(new RecordKey(BaseGame, 0x500 + (uint)index), (index % 2 - 1, index / 2 - 1)))];

        var one = Plan(targets, removed, rocks, cells, workers: 1);
        var eight = Plan(targets, removed, rocks, cells, workers: 8);

        Assert.Equal(one.Objects.Select(Describe), eight.Objects.Select(Describe));
        Assert.Equal(one.Cells.Select(Describe), eight.Cells.Select(Describe));
        Assert.Contains(one.Objects, judged => judged.IsRestored);
        Assert.Contains(one.Objects, judged => !judged.IsRestored);
    }

    /// <summary>A base-game rock the cleaned mod moved from a cleared place into the ring around its campfire, whose camp still stands, goes back with a copy left in the ring.</summary>
    [Fact]
    public void AMovedRockAroundAStandingCampfireGoesBackWithACopy()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>The camp is cleared but its campfire stands, and the visible left-behind step measures the rock around it.</summary>
    [Fact]
    public void AMovedRockBesideTheStandingCampfireOfAClearedCampGoesBackWithACopy()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround).Objects);

        Assert.Equal(AreaVerdict.Cleared, judged.WhereItIsNow!.Verdict);
        Assert.Equal(new CloneJudged(CloneNeed.NeighbourOfStandingObject, RefusedBy: null), judged.Cloning);
        Assert.True(judged.IsRestored);
    }

    /// <summary>The same cleared camp without the left-behind step, or with the campfire removed too: nothing holds the rock, so it goes back as before.</summary>
    [Theory]
    [InlineData(false, new[] { 0, 3, 4 })]
    [InlineData(true, new[] { 0, 1, 3, 4 })]
    public void AMovedRockThatNoStandingObjectHoldsGoesBackWithoutACopy(bool leftBehindOn, int[] removed)
    {
        var judged = Assert.Single(Plan(CampScene(), removed, [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: leftBehindOn ? LookAround : null).Objects);

        Assert.Equal(((CloneJudged?)null, true), (judged.Cloning, judged.IsRestored));
    }

    /// <summary>The campfire of the cleared camp holds the rock, but the rock may not be copied, so it stays where the cleaned mod put it.</summary>
    [Fact]
    public void AMovedRockTheStandingCampfireHoldsThatMayNotBeCopiedStays()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: TestBaseFacts.Unresolved, visibleLeftBehind: LookAround).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.NeighbourOfStandingObject, CloneRefusal.NotPlainScenery), judged.Cloning);
        Assert.Equal((false, (AreaVerdict?)null, (PlacedRefusal?)null), (judged.IsRestored, judged.KeptForArea, judged.Refused));
        Assert.Equal(
            "the cleaned mod's standing content holds it and it may not be copied (not plain scenery)",
            RestoreOutcomes.KeptBecause(judged, key => key.ToString()));
    }

    public static TheoryData<string, CloneRefusal> RefusedCopies => new()
    {
        { "not plain scenery, such as a door", CloneRefusal.NotPlainScenery },
        { "base not in the load order", CloneRefusal.NotPlainScenery },
        { "base with distant LOD", CloneRefusal.HasDistantLod },
        { "visible when distant", CloneRefusal.HasDistantLod },
        { "persistent", CloneRefusal.Persistent },
        { "scripted, linked or with an enable parent", CloneRefusal.HoldsMoreThanPlacement },
    };

    [Theory]
    [MemberData(nameof(RefusedCopies))]
    public void AMovedRockAroundAStandingCampfireThatMayNotBeCopiedIsKeptForItsArea(string rock, CloneRefusal expected)
    {
        var (change, bases) = rock switch
        {
            "not plain scenery, such as a door" => (MovedRock(), new SceneryBases(Plain: false, DistantLod: false)),
            "base not in the load order" => (MovedRock(), TestBaseFacts.Unresolved),
            "base with distant LOD" => (MovedRock(), new SceneryBases(Plain: true, DistantLod: true)),
            "visible when distant" => (MovedRock() with { ReferenceNow = TestPlacedRecords.PlainReference with { IsVisibleWhenDistant = true } }, PlainScenery),
            "persistent" => (MovedRock() with { IsPersistent = true }, PlainScenery),
            _ => (MovedRock() with { ReferenceNow = TestPlacedRecords.PlainReference with { HoldsOnlyPlacement = false } }, PlainScenery),
        };

        var judged = Assert.Single(Plan(CampScene(), removed: [0], [change], bases: bases).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, expected), judged.Cloning);
        Assert.Equal((false, (AreaVerdict?)AreaVerdict.StillStanding), (judged.IsRestored, judged.KeptForArea));
    }

    public static TheoryData<string> RocksWithoutCopyRule => ["base swap in place", "turned only", "rescaled only", "position held by a later plugin", "copy exists", "linked from the cleaned mod", "old place stands"];

    [Theory]
    [MemberData(nameof(RocksWithoutCopyRule))]
    public void TheCopyRuleDoesNotApplyToARockThatWasNotMovedOrWhoseOldPlaceStaysOrThatHasACopy(string rock)
    {
        var rockKey = MovedRock().Key;
        var (targets, change, removed) = rock switch
        {
            "base swap in place" => (CampScene(TargetOrigin.Replaced), MovedRock() with { Earlier = MovedRock().Earlier with { Undo = PlacedParts.BaseAndData | PlacedParts.Position } }, new[] { 0 }),
            "turned only" => (CampScene(), MovedRock(undo: PlacedParts.Rotation), new[] { 0 }),
            "rescaled only" => (CampScene(), MovedRock(undo: PlacedParts.Scale), new[] { 0 }),
            "position held by a later plugin" => (CampScene(), MovedRock(undo: PlacedParts.BaseAndData), new[] { 0 }),
            "copy exists" => (CampScene(Own(5, new Vector3(-3000, 0, 0)) with { CloneOf = rockKey }), MovedRock(), new[] { 0 }),
            "linked from the cleaned mod" => (CampScene(), MovedRock() with { LinkedFromCleanedMod = true }, new[] { 0 }),
            _ => (CampScene(), MovedRock(), Array.Empty<int>()),
        };

        var judged = Assert.Single(Plan(targets, removed, [change], bases: PlainScenery).Objects);

        Assert.Null(judged.Cloning);
    }

    /// <summary>
    /// A later mod's block stands 60 units from the centre of the rock beside the standing campfire. A zone reaches the rock's half size,
    /// 20 units, plus the too-close distance: 30 units by the moved-object settings, 70 or 50 by the cleaned mod's own, which judge the copy.
    /// </summary>
    [Theory]
    [InlineData(50f, true)]
    [InlineData(30f, false)]
    public void ACopyTooCloseToALaterModsObjectByTheCleanedModsOwnSettingsIsRefused(float ownDistance, bool refused)
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, others: [LaterBlock(Camp + new Vector3(-60, 0, 0))], cleanedModsObjects: OwnTooClose(ownDistance)).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, refused ? CloneRefusal.TooCloseAsCleanedModsObject : null), judged.Cloning);
        Assert.Equal((!refused, refused ? AreaVerdict.StillStanding : (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>In the cleared camp, the copy the standing campfire needs is too close to a later mod's block by the cleaned mod's own settings, so the rock stays.</summary>
    [Fact]
    public void AMovedRockTheStandingCampfireHoldsWhoseCopyIsTooCloseAsTheCleanedModsObjectStays()
    {
        var judged = Assert.Single(
            Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround, others: [LaterBlock(Camp + new Vector3(-60, 0, 0))], cleanedModsObjects: OwnTooClose(50f)).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.NeighbourOfStandingObject, CloneRefusal.TooCloseAsCleanedModsObject), judged.Cloning);
        Assert.Equal((false, (AreaVerdict?)null, (PlacedRefusal?)null), (judged.IsRestored, judged.KeptForArea, judged.Refused));
        Assert.Equal(
            "the cleaned mod's standing content holds it and it may not be copied (too close as the cleaned mod's object)",
            RestoreOutcomes.KeptBecause(judged, key => key.ToString()));
    }

    /// <summary>
    /// In the cleared camp a second rock goes back to 60 units from the copy's centre and keeps a scale Later.esp set. Whether or not
    /// the removal steps believe Later.esp, the next patcher reads the rock the patch put back as support only, never as a cause, so
    /// the copy stays.
    /// </summary>
    [Theory]
    [InlineData(VersionTrust.None)]
    [InlineData(VersionTrust.Full)]
    public void ACopyBesideARockGoingBackStaysWhateverTheTrustOfThePartsTheRockKeeps(VersionTrust laterTrust)
    {
        var second = ChangedRock(0x901, Camp + new Vector3(-60, 0, 0));
        var keepingScale = second with { BaseNow = Block.Base, Earlier = second.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };

        var result = Plan(
            CampScene(), DecisionsRemoving(CampScene(), 0, 3, 4), [MovedRock(), keepingScale], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround,
            cleanedModsObjects: OwnTooClose(50f), laterTrust: laterTrust);

        Assert.Equal(new CloneJudged(CloneNeed.NeighbourOfStandingObject, RefusedBy: null), result.Objects[0].Cloning);
    }

    /// <summary>The cleaned mod's own changed-terrain check is asked about the copy, as the cleaned mod's object where the rock stands now; a finding or a stopped check refuses it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACopyTheCleanedModsOwnChangedTerrainCheckFindsOrStopsOnIsRefused(bool stops)
    {
        var terrain = new RecordingRule(stops ? new PlacementCheck(Clash: null, Failure: "InvalidOperationException: broke") : new PlacementCheck(new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()), Failure: null));

        var result = Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, cleanedModsObjects: new(TestTargetGroups.TooCloseOff, terrain, TestTargetGroups.ChangedTerrainOff));

        var judged = Assert.Single(result.Objects);
        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, CloneRefusal.OnChangedTerrainAsCleanedModsObject, stops ? "InvalidOperationException: broke" : null), judged.Cloning);
        Assert.Equal([(Camp, TargetOrigin.CleanedMod, (RecordKey?)MovedRock().Key)], terrain.Checked.Select(asked => (asked.Transform.Position, asked.Origin, asked.CloneOf)).Distinct());
        Assert.Equal(stops ? [(RestoreWhereRemovedParts.ObjectCheck, "InvalidOperationException: broke")] : [], result.Failures.Select(failure => (failure.Part, failure.Failure)));
    }

    /// <summary>
    /// In the cleared camp rock A goes back with a copy the standing campfire needs. Rock B, which the cleaned mod moved into a second
    /// camp that still stands, goes back with a copy too, to 60 units from A's copy, and keeps a scale Later.esp set. Whether or not
    /// the removal steps believe Later.esp, the next patcher reads B as support only, so both go back with their copies.
    /// </summary>
    [Theory]
    [InlineData(VersionTrust.None)]
    [InlineData(VersionTrust.Full)]
    public void ARockGoingBackWithACopyIsNoCauseForAnotherCopy(VersionTrust laterTrust)
    {
        var secondCamp = new Vector3(5000, 5000, 0);
        var secondRockKey = new RecordKey(BaseGame, 0x902);
        var targets = CampScene(
            Own(5, secondCamp) with { Origin = TargetOrigin.Moved, Key = secondRockKey },
            Own(6, secondCamp + new Vector3(60, 0, 0)),
            Own(7, secondCamp + new Vector3(0, 150, 0)),
            Own(8, secondCamp + new Vector3(0, -150, 0)));
        var second = ChangedRock(secondRockKey.Id, Camp + new Vector3(-60, 0, 0), target: new TargetId(5));
        var keepingScale = second with { Earlier = second.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };

        var result = Plan(
            targets, DecisionsRemoving(targets, 0, 3, 4), [MovedRock(), keepingScale], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround,
            cleanedModsObjects: OwnTooClose(50f), laterTrust: laterTrust);

        Assert.Equal((new CloneJudged(CloneNeed.NeighbourOfStandingObject, RefusedBy: null), true), (result.Objects[0].Cloning, result.Objects[0].IsRestored));
        Assert.Equal((new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null), true), (result.Objects[1].Cloning, result.Objects[1].IsRestored));
    }

    /// <summary>
    /// A navmesh the cleaned mod added, which stays changed, has a triangle whose centre lies inside the rock in the camp ring, so
    /// actors would walk on a copy: the copy is lost and the rock stays for its area. A navmesh beside the rock leaves the copy.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(100f, false)]
    public void ARockWhoseCopyActorsWouldWalkOnStaysForItsArea(float navmeshOffset, bool walkedOn)
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, navmeshes: [AddedNavmeshAround(Camp + new Vector3(0, navmeshOffset, 0))]).Objects);

        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, walkedOn ? CloneRefusal.ActorsWalkOnIt : null), judged.Cloning);
        Assert.Equal((!walkedOn, walkedOn ? AreaVerdict.StillStanding : (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>
    /// In the cleared camp the rock goes back with a copy the campfire needs, and a second base game rock goes back to 30 units from
    /// the rock's place: the copy stands there, so the second rock is refused. Without the left-behind step no copy is planned, the
    /// rock leaves its place and the second rock goes back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoingBackIntoTheCopysPlaceIsRefused(bool leftBehindOn)
    {
        var second = ChangedRock(0x901, Camp + new Vector3(-30, 0, 0)) with { BaseNow = Block.Base };

        var result = Plan(CampScene(), removed: [0, 3, 4], [MovedRock(), second], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: leftBehindOn ? LookAround : null);

        Assert.Equal((leftBehindOn ? new CloneJudged(CloneNeed.NeighbourOfStandingObject, RefusedBy: null) : null, true), (result.Objects[0].Cloning, result.Objects[0].IsRestored));
        Assert.Equal(leftBehindOn ? new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, RockKey, Failure: null) : null, result.Objects[1].Refused);
    }

    [Fact]
    public void ALeftBehindStepThatIsSwitchedOffMeasuresNoNeighbours()
    {
        var targets = CampScene();
        var decisions = DecisionsRemoving(targets);
        var caches = CachesOf(targets);
        List<TargetId> visible = [new TargetId(99)];
        List<TargetId> invisible = [new TargetId(99)];

        LeftBehindRules.CreateVisibleNeighbours(targets, decisions, caches, settings: null).Around(targets[1], visible);
        LeftBehindRules.CreateInvisibleNeighbours(targets, decisions, caches, settings: null).Around(targets[1], invisible);

        Assert.Equal((0, 0), (visible.Count, invisible.Count));
    }

    /// <summary>The visible step measures the visible targets around a visible one; the invisible step those around an invisible one, each only around its own kind.</summary>
    [Fact]
    public void EachLeftBehindStepMeasuresTheVisibleTargetsAroundAnObjectOfItsKind()
    {
        var marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = CampScene(TestTargets.Create(5, TestTargets.At(Camp + new Vector3(-60, 0, 0)), marker, TestTargets.Space));
        var decisions = DecisionsRemoving(targets);
        var caches = CachesOf(targets, PlainScenery);
        var visibleStep = LeftBehindRules.CreateVisibleNeighbours(targets, decisions, caches, LookAround);
        var invisibleStep = LeftBehindRules.CreateInvisibleNeighbours(targets, decisions, caches, new InvisibleLeftBehindSettings(LookAround.Surroundings));

        Assert.Equal([2, 3, 4], NeighbourIndexes(visibleStep, targets[1]));
        Assert.Empty(NeighbourIndexes(visibleStep, targets[5]));
        Assert.Equal([1, 2, 3, 4], NeighbourIndexes(invisibleStep, targets[5]));
        Assert.Empty(NeighbourIndexes(invisibleStep, targets[1]));
    }

    private static int[] NeighbourIndexes(ILeftBehindNeighbours step, TargetObject target)
    {
        List<TargetId> neighbours = [];
        step.Around(target, neighbours);
        return [.. neighbours.Select(neighbour => neighbour.Index)];
    }

    private static readonly Vector3 Camp = new(5000, 0, 0);

    private static readonly IBaseFacts PlainScenery = TestBaseFacts.PlainScenery;

    private static readonly VisibleLeftBehindSettings LookAround =
        new(new LeftBehindSurroundings(LookAround: 200f, MinOccupiedDirections: 1, DirectionClearedPercent: 50, ClearedDirectionsPercent: 50), MinObjectsAround: 1);

    /// <summary>
    /// The cleaned mod's object 0 near the rock's old place; at the camp its campfire 1 beside the rock 2 it moved there, and its
    /// objects 3 and 4 farther out; then <paramref name="more"/>.
    /// </summary>
    private static ImmutableArray<TargetObject> CampScene(params TargetObject[] more) => CampScene(TargetOrigin.Moved, more);

    private static ImmutableArray<TargetObject> CampScene(TargetOrigin rockOrigin, params TargetObject[] more) =>
    [
        Own(0, new Vector3(100, 0, 0)),
        Own(1, Camp + new Vector3(60, 0, 0)),
        Own(2, Camp) with { Origin = rockOrigin, Key = RockKey },
        Own(3, Camp + new Vector3(0, 150, 0)),
        Own(4, Camp + new Vector3(0, -150, 0)),
        .. more,
    ];

    /// <summary>The base game rock the cleaned mod moved into the camp of <see cref="CampScene(TargetObject[])"/>.</summary>
    private static readonly RecordKey RockKey = new(BaseGame, 0x900);

    /// <summary>The rock of <see cref="CampScene(TargetObject[])"/>, which goes back to the old place at the origin.</summary>
    private static RecordChangedByCleanedMod MovedRock(PlacedParts undo = PlacedParts.Position) => ChangedRock(RockKey.Id, Vector3.Zero, target: new TargetId(2), undo: undo);

    /// <summary>A block of Later.esp that stands now; its record is a change in the world without the cleaned mod.</summary>
    private static OtherObject LaterBlock(Vector3 position) =>
        TestShapes.Placed(ModKey.FromNameAndExtension("Later.esp"), 0, Block.Base, position) with { ChangeWithoutCleanedMod = new ObjectChange(Later, AcceptedFrom: null) };

    /// <summary>The cleaned mod's own objects judged too close within <paramref name="distance"/> units, with a changed-terrain check that finds nothing.</summary>
    private static CleanedModsObjectRules OwnTooClose(float distance) =>
        TestTargetGroups.None.CleanedModsObjects with { TooClose = Settings.TooClose with { DistanceInUnits = distance } };

    /// <summary>A navmesh the cleaned mod added, with one triangle 20 units wide around <paramref name="centre"/>.</summary>
    private static NavmeshChangedByCleanedMod AddedNavmeshAround(Vector3 centre)
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

    /// <summary>A rule that answers every check the same way and keeps each object it was asked about.</summary>
    private sealed class RecordingRule(PlacementCheck answer) : IPlacementRule
    {
        public List<TargetObject> Checked { get; } = [];

        public IPlacementTries StartTries(TargetObject target)
        {
            lock (Checked) Checked.Add(target);
            return new Tries(answer);
        }

        private sealed class Tries(PlacementCheck answer) : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => answer;

            public void Dispose()
            {
            }
        }
    }

    private sealed class SceneryBases(bool Plain, bool DistantLod) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record) with { Resolved = true, IsPlainScenery = Plain, HasDistantLod = DistantLod };
    }

    private static string Describe(PlacedChangeJudged judged) => $"{judged.Change.Key} {judged.Never} {judged.OldPlace} {judged.WhereItIsNow} {judged.Refused} {judged.IsRestored}";

    private static string Describe(CellChangeJudged judged) => $"{judged.Change.Cell} {string.Join(", ", judged.Undone)} {judged.Never} {judged.Area} {judged.IsRestored}";

    private static Vector3 RandomPoint(Random random) => new((random.NextSingle() * 2 - 1) * 4000, (random.NextSingle() * 2 - 1) * 4000, 0);

    private static TargetObject Own(int index, Vector3 position, TestStatic? model = null) =>
        TestTargets.Create(index, TestTargets.At(position), (model ?? Block).Base, TestTargets.Space);

    /// <summary>A rock of the base game that the cleaned mod changed, which goes back to <paramref name="oldPlace"/>.</summary>
    private static RecordChangedByCleanedMod ChangedRock(
        uint id, Vector3 oldPlace, PluginName? winner = null, bool linked = false, TargetId? target = null, PlacedParts undo = PlacedParts.Position) =>
        new(
            new RecordKey(BaseGame, id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            winner ?? TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f)) with { Undo = undo },
            target,
            linked,
            PresenceBefore: default);

    /// <summary>A cell of the base game whose music the cleaned mod changed and whose location a later plugin changed again.</summary>
    private static CellChangedByCleanedMod ChangedCell(RecordKey cell, (int X, int Y)? grid)
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

    private static RemovalDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, params int[] removed) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(TooClose)]);

    private static ProposedRemoval TooClose(int index) => new(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize()));

    private static RestoreWhereRemovedResult Plan(
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
        IEnumerable<NavmeshChangedByCleanedMod>? navmeshes = null) =>
        Plan(
            targets, DecisionsRemoving(targets, removed), records, cells, settings, spaces, workers, bases: bases, visibleLeftBehind: visibleLeftBehind, others: others,
            cleanedModsObjects: cleanedModsObjects, navmeshes: navmeshes);

    /// <param name="otherModsObjects">Switched off when null.</param>
    private static RestoreWhereRemovedResult Plan(
        ImmutableArray<TargetObject> targets,
        IRunDecisions final,
        IEnumerable<RecordChangedByCleanedMod> records,
        IEnumerable<CellChangedByCleanedMod>? cells = null,
        WhereRemovedRestoreSettings? settings = null,
        IReadOnlyDictionary<RecordKey, SpaceFact>? spaces = null,
        int workers = 1,
        OtherModsObjectsResult? otherModsObjects = null,
        IObjectCachesOfWorlds? worldCaches = null,
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = null,
        IBaseFacts? bases = null,
        VisibleLeftBehindSettings? visibleLeftBehind = null,
        IEnumerable<OtherObject>? others = null,
        CleanedModsObjectRules? cleanedModsObjects = null,
        VersionTrust laterTrust = VersionTrust.None,
        IEnumerable<NavmeshChangedByCleanedMod>? navmeshes = null)
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
        var restore = settings ?? Settings;
        return WhereRemovedRestorer.Plan(
            new(
                new(collected, otherModsObjects ?? OtherModsObjectsResult.SwitchedOff),
                final,
                TestClearedAreas.Of(collected, final, caches, restore),
                new(worldCaches ?? rules.WorldCaches, rules.TooCloseRule(tooClose), rules.HoldersOfObjectsThatStay, TestNavmeshCheck.NoFault),
                TestTargetGroups.None with
                {
                    Accepted = new AcceptedWorld(
                        TestTargets.PatchName,
                        ImmutableDictionary<PluginName, VersionTrust>.Empty.Add(BaseGame, VersionTrust.Full).Add(TestTargets.TargetName, VersionTrust.Full).Add(Later, laterTrust)),
                    LeftBehindNeighbours = [LeftBehindRules.CreateVisibleNeighbours(targets, final, caches, visibleLeftBehind)],
                    CleanedModsObjects = cleanedModsObjects ?? TestTargetGroups.None.CleanedModsObjects,
                },
                new(caches, TestLandPlugin.Flat, Plugins, TestTargets.TargetName),
                new Execution(workers),
                restore,
                () => TimeSpan.Zero));
    }

    private static TestRunCaches CachesOf(ImmutableArray<TargetObject> targets, IBaseFacts? bases = null)
    {
        var objects = TestScenes.Create(targets, [], Shapes);
        return new(Shapes, bases: bases ?? TestBaseFacts.Unresolved, navmeshes: NoNavmeshes, visibleTargets: objects.IndexVisibleTargets, shownTargets: objects.IndexShownTargets);
    }

    /// <summary>A too-close rule whose check stops unexpectedly.</summary>
    private sealed class StoppingRule : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) => new StoppingTries();

        private sealed class StoppingTries : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => new(Clash: null, Failure: "InvalidOperationException: broke");

            public void Dispose()
            {
            }
        }
    }

    private static TestPlacedGuardRules GuardRules(int workers) =>
        TestPlacedGuardRules.Create(
            Shapes,
            TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []),
            Plugins,
            new OtherModsObjectsSettings(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap: 1f, SupportLostFraction: 0.5f),
            TestGround.NoTerrain(),
            workers);
}
