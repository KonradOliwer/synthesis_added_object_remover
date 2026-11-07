using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The removal steps' follow-up asked about the candidates of the restore leaving where they stand now, over in-memory scenes on flat
/// ground: a base game rock the cleaned mod moved, which leaves as an object of any plugin moved back to its earlier place, and a crate
/// of another plugin the cleaned mod moved far enough to count among its targets, which leaves as a target object restored to its earlier
/// version. Pebbles of the cleaned mod and other plugins' lanterns rest on them.
/// </summary>
public sealed class FollowUpOfGoingBackTests
{
    private const float HalfSize = 20f;
    private const float Top = 2 * HalfSize;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("FollowUpLeavingShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\followupleaving\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfSize), new Vector3(HalfSize))));
    private static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x802), @"test\followupleaving\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));
    private static readonly TestStatic Pebble = new(new FormKey(ShapeMod, 0x803), @"test\followupleaving\pebble.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))));
    /// <summary>Another model of a block's shape and size.</summary>
    private static readonly TestStatic Boulder = new(new FormKey(ShapeMod, 0x804), @"test\followupleaving\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-HalfSize), new Vector3(HalfSize))));
    /// <summary>How far a plate's top lies above its bottom: twice the touch gap, so what rests on a plate floats once it is gone.</summary>
    private const float PlateThickness = 2f;

    private const float BowlHalfWidth = 25f;
    private const float BowlFloor = 4f;

    /// <summary>
    /// A bowl's width, as thin as <see cref="PlateThickness"/>, its bottom where a block's is: a bowl on it touches it with more than half
    /// of what it touches when a block rests on its floor.
    /// </summary>
    private static readonly TestStatic Plate = new(
        new FormKey(ShapeMod, 0x805),
        @"test\followupleaving\plate.nif",
        TestMeshes.BoxTriangles(new Box(new Vector3(-BowlHalfWidth, -BowlHalfWidth, -HalfSize), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + PlateThickness))));

    /// <summary>
    /// Where the post of a bowl starts along x and y: a block in the middle of the bowl is this far beyond its own corner, within the
    /// touch gap along each axis, so the post lies in its contact area, and farther than the gap diagonally, so the post never touches it.
    /// </summary>
    private const float PostFrom = HalfSize + 0.8f;

    private const float PostHeight = 10f;

    /// <summary>A floor with its bottom where a block's is and a post on one corner, which rises beside a block resting on the floor.</summary>
    private static readonly TestStatic Bowl = new(
        new FormKey(ShapeMod, 0x806),
        @"test\followupleaving\bowl.nif",
        [
            .. TestMeshes.BoxTriangles(new Box(new Vector3(-BowlHalfWidth, -BowlHalfWidth, -HalfSize), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + BowlFloor))),
            .. TestMeshes.BoxTriangles(new Box(new Vector3(PostFrom, PostFrom, -HalfSize + BowlFloor), new Vector3(BowlHalfWidth, BowlHalfWidth, -HalfSize + BowlFloor + PostHeight))),
        ]);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "FollowUpLeavingData", Block, Lantern, Pebble, Boulder, Plate, Bowl));

    /// <summary>A base the game shows, whose record and mesh the shapes do not know, like a marker without a model.</summary>
    private static readonly BaseKey WithoutMesh = TestVisibility.TaggedBase(ObjectVisibility.Visible);

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey MasterMod = ModKey.FromNameAndExtension("Master.esm");
    private static readonly ModKey ListedMod = ModKey.FromNameAndExtension("Listed.esp");
    private static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly ModKey OwnChangesMod = ModKey.FromNameAndExtension("OwnChanges.esp");
    private static readonly ModKey NeverMod = ModKey.FromNameAndExtension("Never.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();

    private static readonly PluginName[] PluginList =
    [
        BaseGame, MasterMod.ToPluginName(), ListedMod.ToPluginName(), TestTargets.TargetName, LaterMod.ToPluginName(), OwnChangesMod.ToPluginName(),
        NeverMod.ToPluginName(), TestTargets.PatchName,
    ];

    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);

    /// <summary>Master.esm is trusted only as a master of the listed trusted mod.</summary>
    private static readonly ModTrustTable Trust = TestModTrust.Of(
        TestTargets.TargetName,
        TestTargets.PatchName,
        trusted: [ListedMod.ToPluginName()],
        untrusted: [LaterMod.ToPluginName()],
        trustedForOwnChanges: [OwnChangesMod.ToPluginName()],
        mastersOfTrusted: [MasterMod.ToPluginName()],
        baseGame: [BaseGame],
        neverTrusted: [NeverMod.ToPluginName()]);

    private static readonly Vector3 RockNow = new(500, 500, HalfSize);
    private static readonly Vector3 CrateNow = new(1000, 500, HalfSize);
    private static readonly Vector3 SecondRockNow = new(1500, 500, HalfSize);

    /// <summary>The rock's candidate index: the candidates are listed rock, crate, second rock.</summary>
    private const int RockCandidate = 0;
    private const int CrateCandidate = 1;
    private const int SecondRockCandidate = 2;
    private const int ThirdRockCandidate = 3;

    /// <summary>The cleaned mod's crate, the first target object.</summary>
    private const int CrateTarget = 0;

    /// <summary>The record number of a lantern, which no rock's record shares.</summary>
    private const int LanternIndex = 5;

    /// <summary>The record number of a block beside the rock.</summary>
    private const int BesideIndex = 6;

    /// <summary>The lantern's id: the rock is object 0, the others follow from 1.</summary>
    private static readonly OtherId FirstOther = new(1);

    /// <summary>
    /// The crate goes back and the rock leaves. The pebble on the crate and the one on the rock go with them; a lantern of a master of a
    /// trusted mod on the rock is pulled down, and the pebble on it goes with it; an unrelated mod's lantern on the rock stays. The
    /// pulled lantern names what it lost: the rock most, then the pebble that touched its top and left with it.
    /// </summary>
    [Fact]
    public void TheRuleFollowsUpWhatRestsOnTheObjectsLeavingAndOnWhatTheyTakeAlong()
    {
        var scene = new Scene
        {
            Pebbles = [CrateNow + new Vector3(0, 0, HalfSize), RockNow + new Vector3(-10, -10, HalfSize), RockNow + new Vector3(10, 10, HalfSize + 6)],
            Others = [Placed(MasterMod, LanternIndex, Lantern, RockNow + new Vector3(10, 10, HalfSize)), Placed(LaterMod, LanternIndex, Lantern, RockNow + new Vector3(-10, 10, HalfSize))],
        };
        var final = scene.Decisions();

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), GoingBack(final, scene), scene.RockLeaving());

        Assert.Equal(
            [
                (new TargetId(1), Supporter.Target(CrateTarget)),
                (new TargetId(2), Supporter.Placed(new OtherId(0))),
                (new TargetId(3), Supporter.Placed(FirstOther)),
            ],
            outcomes.Targets.Select(followed => (followed.Target, Assert.Single(followed.LostFrom))));
        Assert.All(outcomes.Targets, followed => Assert.IsType<Decision.Removed>(followed.Decision));
        var lantern = Assert.Single(outcomes.Others);
        Assert.Equal((FirstOther, OtherModsObjectFate.PulledDown, Top), (lantern.Outcome.Object.Id, lantern.Outcome.Fate, lantern.Outcome.Drop));
        Assert.Equal([Supporter.Placed(new OtherId(0)), Supporter.Target(3)], lantern.LostFrom.AsEnumerable());
        Assert.Empty(outcomes.HeldUp);
    }

    /// <summary>
    /// A lantern rests on the rock. The follow-up pulls it down when its plugin is trusted in full, even only as a master of a trusted
    /// mod, and the restore writes the pull-down, started by the rock, which goes back; the lantern of every other plugin is left as it is.
    /// </summary>
    [Theory]
    [InlineData("Base.esm", true)]
    [InlineData("Master.esm", true)]
    [InlineData("Listed.esp", true)]
    [InlineData("Later.esp", false)]
    [InlineData("OwnChanges.esp", false)]
    [InlineData("Never.esp", false)]
    public void OnlyAnObjectOfAPluginTrustedInFullIsFollowedUp(string plugin, bool followed)
    {
        var lantern = Placed(ModKey.FromNameAndExtension(plugin), LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize));
        var scene = new Scene { Others = [lantern] };
        var final = scene.Decisions();

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, scene.RockLeaving());
        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(followed ? [FirstOther] : [], outcomes.Others.Select(other => other.Outcome.Object.Id));
        Assert.Empty(outcomes.Targets);
        Assert.Empty(checks.Written.Targets);
        Assert.Equal(followed ? [(lantern.Key, RockStarter)] : [], WrittenOthers(checks));
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// Nothing leaves, but the flat ground of cell (0, 0) goes down from 0 to −100 between grid points (20, 12) and (27, 20). A base game
    /// lantern resting on it there loses its support to the ground alone and is pulled down onto the ground after; a later mod's lantern
    /// beside it is left as it is (decision 33), and the objects away from that ground are not judged.
    /// </summary>
    [Fact]
    public void GroundGoingDownLeavesOnlyAnObjectOfAPluginTrustedInFullWithoutSupport()
    {
        var trusted = Placed(BaseGameMod, LanternIndex, Lantern, new Vector3(3000, 2000, 0));
        var later = Placed(LaterMod, BesideIndex, Lantern, new Vector3(3200, 2000, 0));
        var scene = new Scene { Others = [trusted, later] };
        var final = scene.Decisions();
        const float Lowered = -100f;
        static bool IsLowered(int point) => point % TerrainGrid.VerticesPerSide is >= 20 and <= 27 && point / TerrainGrid.VerticesPerSide is >= 12 and <= 20;
        float[] heights = [.. Enumerable.Range(0, TerrainGrid.VertexCount).Select(point => IsLowered(point) ? Lowered : 0f)];
        var planned = new TerrainHeights(cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? heights : null, space => space);
        var step = TerrainGrid.VertexSpacing;
        var loweredBox = new Box(new Vector3(20 * step, 12 * step, Lowered), new Vector3(27 * step, 20 * step, 0));
        var land = new LandAfter(planned, [new LoweredGround(TestTargets.Space, new OrientedBox(loweredBox.Center, Mat3.Identity, loweredBox.HalfExtents))], TestGround.FlatAtZero(), []);

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, new ObjectsLeaving([], [], [], [], [], [], land));

        var pulled = Assert.Single(outcomes.Others);
        Assert.Equal((trusted.Key, OtherModsObjectFate.PulledDown), (pulled.Outcome.Object.Key, pulled.Outcome.Fate));
        Assert.Equal(Lowered, pulled.Outcome.NewPosition.Z, tolerance: 0.5f);
        Assert.Equal([Supporter.Terrain], pulled.LostFrom.AsEnumerable());
        Assert.Empty(outcomes.Targets);
    }

    /// <summary>
    /// A base game lantern rests on the rock, which leaves. The ground of cell (0, 0) goes down only at its far corner, but the box of
    /// ground going down given spans the whole cell: the lantern lost the rock alone, since the ground under it does not go down.
    /// </summary>
    [Fact]
    public void AnObjectGoingWithWhatLeavesNamesTheGroundOnlyWhereTheGroundUnderItGoesDown()
    {
        var lantern = Placed(BaseGameMod, LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize));
        var scene = new Scene { Others = [lantern] };
        var final = scene.Decisions();
        float[] heights = [.. Enumerable.Range(0, TerrainGrid.VertexCount).Select(point => point == TerrainGrid.VertexCount - 1 ? -100f : 0f)];
        var planned = new TerrainHeights(cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? heights : null, space => space);
        var cell = new Box(new Vector3(0, 0, -100), new Vector3(ExteriorGrid.CellSize, ExteriorGrid.CellSize, 0));
        var land = new LandAfter(planned, [new LoweredGround(TestTargets.Space, new OrientedBox(cell.Center, Mat3.Identity, cell.HalfExtents))], TestGround.FlatAtZero(), []);

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, scene.RockLeaving() with { Land = land });

        var pulled = Assert.Single(outcomes.Others);
        Assert.Equal(lantern.Key, pulled.Outcome.Object.Key);
        Assert.Equal([Supporter.Placed(new OtherId(0))], pulled.LostFrom.AsEnumerable());
    }

    /// <summary>
    /// A lantern rests on the rock, created by one plugin and won by another, past the patch when the patch wins it. It is pulled down
    /// only when its creator is trusted in full and its winner past the patch is trusted in full or the cleaned mod: a base game lantern
    /// an untrusted mod moved there, or an untrusted mod's lantern a trusted patch overrides, is left as it is. The cleaned mod's own
    /// lantern a trusted mod holds is never followed up.
    /// </summary>
    [Theory]
    [InlineData("Base.esm", "Listed.esp", "Listed.esp", true)]
    [InlineData("Base.esm", "Target.esp", "Target.esp", true)]
    [InlineData("Base.esm", "Synthesis.esp", "Master.esm", true)]
    [InlineData("Base.esm", "Later.esp", "Later.esp", false)]
    [InlineData("Base.esm", "OwnChanges.esp", "OwnChanges.esp", false)]
    [InlineData("Base.esm", "Synthesis.esp", "Later.esp", false)]
    [InlineData("Later.esp", "Listed.esp", "Listed.esp", false)]
    [InlineData("Target.esp", "Synthesis.esp", "Listed.esp", false)]
    public void AnObjectIsFollowedUpOnlyWhenItsCreatorAndItsWinnerPastThePatchAreTrustedInFull(string creator, string winner, string winnerPastPatches, bool followed)
    {
        var lantern = Placed(ModKey.FromNameAndExtension(creator), LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize)) with
        {
            WinningMod = new PluginName(winner),
            WinningModPastPatches = new PluginName(winnerPastPatches),
        };
        var scene = new Scene { Others = [lantern] };
        var final = scene.Decisions();

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, scene.RockLeaving());
        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(followed ? [FirstOther] : [], outcomes.Others.Select(other => other.Outcome.Object.Id));
        Assert.Equal(followed ? [(lantern.Key, RockStarter)] : [], WrittenOthers(checks));
    }

    /// <summary>
    /// With KeepSupportingObject the rock is kept as it is when it holds up a lantern of a plugin trusted in full, as the removal steps
    /// keep a supporting object; the lantern is only found, never pulled down or removed. A lantern of any other plugin keeps nothing.
    /// </summary>
    [Theory]
    [InlineData("Base.esm", true)]
    [InlineData("Master.esm", true)]
    [InlineData("Listed.esp", true)]
    [InlineData("Later.esp", false)]
    [InlineData("OwnChanges.esp", false)]
    public void WithKeepSupportingObjectACandidateHoldingUpAnObjectOfAPluginTrustedInFullStays(string plugin, bool kept)
    {
        var lantern = Placed(ModKey.FromNameAndExtension(plugin), LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize));
        var scene = new Scene { Others = [lantern], Handling = OtherModsObjectsHandling.KeepSupportingObject };
        var final = scene.Decisions();

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, scene.RockLeaving());
        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Empty(outcomes.Others);
        Assert.Equal(kept ? [FirstOther] : [], outcomes.HeldUp.Select(other => other.Object.Id));
        Assert.All(outcomes.HeldUp, other => Assert.Equal([Supporter.Placed(new OtherId(0))], other.LostFrom.AsEnumerable()));
        Assert.Equal(kept ? new Dictionary<int, RecordKey> { [RockCandidate] = lantern.Key } : [], checks.HoldingUp);
        Assert.Empty(checks.Written.Others);
    }

    /// <summary>
    /// A pebble of the cleaned mod rests on its crate, which goes back to its earlier version: the pebble is removed by its when-removed
    /// rule, started by the crate.
    /// </summary>
    [Fact]
    public void ATargetCandidateGoingBackStartsTheFollowUpOfWhatRestsOnIt()
    {
        var scene = new Scene { Pebbles = [CrateNow + new Vector3(0, 0, HalfSize)] };

        var checks = scene.Check(goingBack: [CrateCandidate]);

        Assert.Equal([(new TargetId(1), TestTargets.Key(CrateTarget).ToString())], WrittenTargets(checks));
        Assert.Null(Assert.Single(checks.Written.Targets).RestoredTo);
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The crate or the rock goes back only in its enable state, which disables it again, so it stays in place but leaves the game:
    /// the pebble on it goes with it.
    /// </summary>
    [Theory]
    [InlineData(CrateCandidate)]
    [InlineData(RockCandidate)]
    public void ACandidateHiddenAgainStartsTheFollowUpOfWhatRestsOnIt(int hidden)
    {
        var now = hidden == CrateCandidate ? CrateNow : RockNow;
        var scene = new Scene { Pebbles = [now + new Vector3(0, 0, HalfSize)], HiddenAgain = hidden };

        var checks = scene.Check(goingBack: [hidden]);

        Assert.Equal([(new TargetId(1), scene.Candidates()[hidden].Key.ToString())], WrittenTargets(checks));
    }

    /// <summary>
    /// The rock goes back only to a slightly different turn about its vertical axis, so it stays under what rests on it: neither the
    /// pebble on it nor the base game lantern with a pebble on top loses its support, and nothing keeps the rock.
    /// </summary>
    [Fact]
    public void ACandidateTurnedBackUnderWhatRestsOnItStartsNothing()
    {
        var scene = new Scene
        {
            Pebbles = [RockNow + new Vector3(-10, -10, HalfSize), RockNow + new Vector3(10, 10, HalfSize + 6)],
            Others = [Placed(BaseGameMod, LanternIndex, Lantern, RockNow + new Vector3(10, 10, HalfSize))],
            RockTurnedBack = true,
        };

        var checks = scene.Check(goingBack: [RockCandidate]);

        AssertNothingFollowed(checks);
    }

    /// <summary>
    /// A pebble of the cleaned mod rests on a base game lantern on the rock: the lantern is pulled down and the pebble goes with it,
    /// so the rock starts the pebble's follow-up. An unrelated mod's lantern stays, so a pebble on it starts nothing.
    /// </summary>
    [Theory]
    [InlineData("Base.esm", true)]
    [InlineData("Later.esp", false)]
    public void ATargetObjectOnAnObjectTheFollowUpTakesAwayTracesBackToTheCandidate(string lanternPlugin, bool started)
    {
        var scene = new Scene
        {
            Pebbles = [RockNow + new Vector3(0, 0, HalfSize + 6)],
            Others = [Placed(ModKey.FromNameAndExtension(lanternPlugin), LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize))],
        };

        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(started ? [(new TargetId(1), RockStarter)] : [], WrittenTargets(checks));
    }

    /// <summary>
    /// The crate goes back with a copy left on the rock, or the second rock does, of the base game or of an unrelated mod; the rock
    /// leaves. The copy is the cleaned mod's content, so the object it stands for is followed up whatever its plugin, and the copy is
    /// lost, since nothing would hold it where it stands.
    /// </summary>
    [Theory]
    [InlineData(CrateCandidate, "Base.esm")]
    [InlineData(SecondRockCandidate, "Base.esm")]
    [InlineData(SecondRockCandidate, "Later.esp")]
    public void ACopyOnACandidateLeavingIsLost(int withCopy, string secondRockPlugin)
    {
        var onTheRock = RockNow + new Vector3(0, 0, Top);
        var scene = new Scene
        {
            CrateAt = withCopy == CrateCandidate ? onTheRock : CrateNow,
            SecondRockAt = withCopy == SecondRockCandidate ? onTheRock : SecondRockNow,
            SecondRockMod = ModKey.FromNameAndExtension(secondRockPlugin),
        };

        var checks = scene.Check(goingBack: [RockCandidate, withCopy], withClone: [withCopy]);

        Assert.Equal([withCopy], checks.CopiesLost);
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The second rock, of an unrelated mod, goes back with a copy left leaning on the rock for about a third of its support and on a
    /// base game block beside it for the rest; the rock leaves. The copy is judged as the cleaned mod's own objects are, by the
    /// also-remove mode and its threshold, not by the other mods' half: it goes with the rock when it touches it, or when the share
    /// it loses reaches the threshold, and the copy is lost.
    /// </summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, 0.5f, true)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, 0.25f, true)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, 0.5f, false)]
    public void ACopyIsJudgedByTheAlsoRemoveMode(FollowUpRemovalMode mode, float lostFraction, bool lost)
    {
        var scene = new Scene
        {
            Others = [Placed(BaseGameMod, BesideIndex, Block, RockNow + new Vector3(45, 0, 0))],
            SecondRockAt = RockNow + new Vector3(28, 0, Top),
            SecondRockMod = LaterMod,
            AlsoRemove = TestPlacedGuardRules.DefaultFollowUp with { Mode = mode, SupportLostFraction = lostFraction },
        };

        var checks = scene.Check(goingBack: [RockCandidate, SecondRockCandidate], withClone: [SecondRockCandidate]);

        Assert.Equal(lost ? [SecondRockCandidate] : [], checks.CopiesLost);
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The second rock, of an unrelated mod, goes back with a copy left on top of the rock or beside it, and the rock leaves. A copy on
    /// the rock goes with it when the rock moves away; it stays when the rock is only turned back, since the rock still touches it
    /// where it stands after, and when the also-remove mode is Nothing, which judges no copy. A copy beside the rock never touched it
    /// and stays.
    /// </summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, false, false, true)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, true, false, false)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, false, false)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, false, true, false)]
    [InlineData(FollowUpRemovalMode.Nothing, false, false, false)]
    public void ACopyGoesWithTheRockOnlyWhenItLosesIt(FollowUpRemovalMode mode, bool rockTurnedBack, bool copyBeside, bool goes)
    {
        var scene = new Scene
        {
            SecondRockAt = RockNow + (copyBeside ? new Vector3(100, 0, 0) : new Vector3(0, 0, Top)),
            SecondRockMod = LaterMod,
            RockTurnedBack = rockTurnedBack,
            AlsoRemove = TestPlacedGuardRules.DefaultFollowUp with { Mode = mode },
        };
        var copy = scene.Objects()[^1];
        var final = scene.Decisions();

        var outcomes = scene.Rule(threads: 1).Find(Standing(final), final, scene.RockLeaving() with { Copies = [copy] });

        Assert.Equal(goes ? [(copy.Id, Supporter.Placed(new OtherId(0)))] : [], outcomes.Copies.Select(followed => (followed.Object.Id, Assert.Single(followed.LostFrom))));
        Assert.Empty(outcomes.Targets);
    }

    /// <summary>
    /// The rock goes back only in its base and data, such as its owner, and stays where it stands. With the same base it keeps its
    /// shape, so the pebble on it keeps its support and nothing starts; with another base it may take another shape, so the pebble
    /// goes with it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACandidateGoingBackInItsDataStartsNothingWhileItKeepsItsShape(bool sameBase)
    {
        var scene = new Scene { Pebbles = [RockNow + new Vector3(0, 0, HalfSize)], RockBaseBack = sameBase ? Block.Base : Lantern.Base };

        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(sameBase ? [] : [(new TargetId(1), RockStarter)], WrittenTargets(checks));
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The follow-up leaves a base game lantern on the rock without support, and the pull finds it still resting where it stands, or
    /// pulls it down: a lantern still resting is not written, one pulled down is, and neither keeps the rock where it stands.
    /// </summary>
    [Theory]
    [InlineData(OtherModsObjectFate.LeftStillTouching, false)]
    [InlineData(OtherModsObjectFate.PulledDown, true)]
    public void OnlyAnObjectThatDoesNotStayRestingIsWritten(OtherModsObjectFate fate, bool written)
    {
        var lantern = Placed(BaseGameMod, LanternIndex, Lantern, RockNow + new Vector3(0, 0, HalfSize)) with { Id = FirstOther };
        var scene = new Scene { Others = [lantern] };
        var leftWithoutSupport = new FollowedOther(new OtherModsObjectOutcome(lantern, fate, LostShare: 1f, Drop: 0f, ReferenceRoles.None), [Supporter.Placed(new OtherId(0))]);

        var checks = scene.Check(goingBack: [RockCandidate], rule: new FixedFollowUp(new FollowUpOutcomes([], [], [leftWithoutSupport], [], [])));

        Assert.Equal(written ? [(lantern.Key, RockStarter)] : [], WrittenOthers(checks));
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// The follow-up never judges a candidate leaving: a target candidate is decided, any other one left out. An answer that takes
    /// along the crate going back, or pulls down the rock going back, is a bug.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFollowUpOfACandidateLeavingIsABug(bool crate)
    {
        var scene = new Scene();
        var outcomes = crate
            ? new FollowUpOutcomes([new FollowedTarget(new TargetId(CrateTarget), new Decision.Removed(new Round(0, RoundKind.Restore), new GoingBackWhereRemoved()), [])], [], [], [], [])
            : new FollowUpOutcomes([], [], [new FollowedOther(new OtherModsObjectOutcome(scene.Rock(), OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 1f, ReferenceRoles.None), [])], [], []);

        Assert.Throws<UnreachableException>(() => scene.Check(goingBack: [RockCandidate, CrateCandidate], rule: new FixedFollowUp(outcomes)));
    }

    /// <summary>A follow-up that answers every question with the same outcomes.</summary>
    private sealed class FixedFollowUp(FollowUpOutcomes outcomes) : IFollowUpOfObjectsLeaving
    {
        public FollowUpOutcomes Find(WorldStanding standing, IRemovalDecisions decided, ObjectsLeaving leaving) => outcomes;
    }

    /// <summary>
    /// A second rock the cleaned mod moved onto the rock, both from 1000 units north, goes back too. When the rock below goes back as
    /// well, the second rock lands on it again and nothing is followed. When the rock below stays, the second rock would float at its
    /// old place: a base game rock is pulled down onto the ground or removed, as the handling says, started by itself; with
    /// KeepSupportingObject, or for an unrelated mod's rock, which the follow-up may not change, it stays where the cleaned mod put it.
    /// </summary>
    [Theory]
    [InlineData(true, "Base.esm", OtherModsObjectsHandling.PullObjectToTheGround, null, false)]
    [InlineData(true, "Later.esp", OtherModsObjectsHandling.PullObjectToTheGround, null, false)]
    [InlineData(false, "Base.esm", OtherModsObjectsHandling.PullObjectToTheGround, OtherModsObjectFate.PulledDown, false)]
    [InlineData(false, "Base.esm", OtherModsObjectsHandling.RemoveFloatingObject, OtherModsObjectFate.Removed, false)]
    [InlineData(false, "Base.esm", OtherModsObjectsHandling.KeepSupportingObject, null, true)]
    [InlineData(false, "Later.esp", OtherModsObjectsHandling.PullObjectToTheGround, null, true)]
    [InlineData(false, "Later.esp", OtherModsObjectsHandling.RemoveFloatingObject, null, true)]
    public void ACandidateGoingBackOntoACandidateThatStaysNeverFloats(bool rockGoesBack, string secondRockPlugin, OtherModsObjectsHandling handling, OtherModsObjectFate? fate, bool stays)
    {
        var secondRockMod = ModKey.FromNameAndExtension(secondRockPlugin);
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top), SecondRockMod = secondRockMod, Handling = handling };

        var checks = scene.Check(goingBack: rockGoesBack ? [RockCandidate, SecondRockCandidate] : [SecondRockCandidate]);

        var secondRock = RockKey(1, secondRockMod);
        Assert.Equal(fate is null ? [] : [(secondRock, secondRock.ToString(), fate.Value)], checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy), other.Outcome.Fate)));
        Assert.Equal(stays ? [SecondRockCandidate] : [], checks.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Empty(checks.Written.Targets);
    }

    /// <summary>A pulled-down candidate is pulled from its old place, where it goes back to, onto the ground.</summary>
    [Fact]
    public void ACandidateWithoutSupportWhereItGoesBackIsPulledDownFromThere()
    {
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top) };

        var checks = scene.Check(goingBack: [SecondRockCandidate]);

        var pulled = Assert.Single(checks.Written.Others).Outcome;
        Assert.Equal(RockNow + new Vector3(0, 1000, Top), pulled.Object.Position);
        Assert.Equal(HalfSize, pulled.NewPosition.Z, tolerance: 0.5f);
    }

    /// <summary>
    /// The crate, another plugin's object the cleaned mod moved onto the rock far enough to count among its targets, goes back to 1000 units
    /// north. With the rock going back too it lands on the rock again; with the rock staying it would float, so it is refused and stays
    /// where the cleaned mod moved it.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ATargetCandidateGoingBackOntoACandidateThatStaysStaysToo(bool rockGoesBack, bool stays)
    {
        var scene = new Scene { CrateAt = RockNow + new Vector3(0, 0, Top) };

        var checks = scene.Check(goingBack: rockGoesBack ? [RockCandidate, CrateCandidate] : [CrateCandidate]);

        Assert.Equal(stays ? [CrateCandidate] : [], checks.ArrivingWhereTheyDoNotFit.Keys);
        AssertNothingFollowed(checks);
    }

    /// <summary>
    /// Three rocks stood stacked 1000 units north; the cleaned mod moved them, still stacked, onto the ground. The bottom rock stays, the
    /// two above it go back and would float. The middle one is pulled down onto the ground, and the top one, judged again once the middle
    /// one landed, is pulled down onto it, so neither floats. A top rock of an unrelated mod, which the follow-up may not change, stays
    /// where the cleaned mod moved it.
    /// </summary>
    [Theory]
    [InlineData("Base.esm")]
    [InlineData("Later.esp")]
    public void AStackGoingBackOntoACandidateThatStaysSettlesFromTheBottomUp(string topPlugin)
    {
        var topMod = ModKey.FromNameAndExtension(topPlugin);
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top), ThirdRockAt = RockNow + new Vector3(0, 0, 2 * Top), ThirdRockMod = topMod };

        var checks = scene.Check(goingBack: [SecondRockCandidate, ThirdRockCandidate]);

        var pulled = checks.Written.Others.Select(other => other.Outcome).ToList();
        var topFollowed = topMod == BaseGameMod;
        Assert.Equal(topFollowed ? [RockKey(1, BaseGameMod), RockKey(2, topMod)] : [RockKey(1, BaseGameMod)], pulled.Select(outcome => outcome.Object.Key));
        Assert.All(pulled, outcome => Assert.Equal(OtherModsObjectFate.PulledDown, outcome.Fate));
        Assert.Equal(HalfSize, pulled[0].NewPosition.Z, tolerance: 0.5f);
        if (topFollowed) Assert.Equal(HalfSize + Top, pulled[1].NewPosition.Z, tolerance: 0.5f);
        Assert.Equal(topFollowed ? [] : [ThirdRockCandidate], checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// A third rock stood across the rock and the second rock, side by side, 1000 units north; the cleaned mod moved all three. Only the
    /// third goes back, with KeepSupportingObject, so it would float there and stays: its refusal names both rocks it lost its support
    /// from, not only the one it lost most from.
    /// </summary>
    [Fact]
    public void AnArrivalThatLosesSeveralSupportsNamesEveryOne()
    {
        var scene = new Scene
        {
            SecondRockAt = RockNow + new Vector3(Top, 0, 0), ThirdRockAt = RockNow + new Vector3(HalfSize, 0, Top), Handling = OtherModsObjectsHandling.KeepSupportingObject,
        };

        var checks = scene.Check(goingBack: [ThirdRockCandidate]);

        var refused = Assert.Single(checks.ArrivingWhereTheyDoNotFit);
        Assert.Equal((ThirdRockCandidate, ArrivalMisfit.LosesSupport), (refused.Key, refused.Value.Misfit));
        Assert.Equal([RockKey(0, BaseGameMod), RockKey(1, BaseGameMod)], refused.Value.Causes.Order(RecordKeyOrder.Comparer));
    }

    /// <summary>
    /// The same stack with the middle rock of an unrelated mod: it would float where it goes back and may not be changed there, so it
    /// stays where the cleaned mod moved it. The top rock, which rested on it where both arrive, is judged again without it there and is
    /// pulled down onto the ground.
    /// </summary>
    [Fact]
    public void AnArrivalWhoseSupportStaysWhereItStandsNowIsJudgedAgainWithoutIt()
    {
        var scene = new Scene
        {
            SecondRockAt = RockNow + new Vector3(0, 0, Top), SecondRockMod = LaterMod, ThirdRockAt = RockNow + new Vector3(0, 0, 2 * Top),
        };

        var checks = scene.Check(goingBack: [SecondRockCandidate, ThirdRockCandidate]);

        var pulled = Assert.Single(checks.Written.Others).Outcome;
        Assert.Equal((RockKey(2, BaseGameMod), OtherModsObjectFate.PulledDown), (pulled.Object.Key, pulled.Fate));
        Assert.Equal(HalfSize, pulled.NewPosition.Z, tolerance: 0.5f);
        Assert.Equal([SecondRockCandidate], checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// 1000 units north a bowl stood on a plate with a rock on its floor; the cleaned mod moved all three, still stacked. The plate
    /// stays, so the bowl, going back, floats twice the touch gap above the ground and is pulled down by that much. The bowl's post still
    /// lies in the rock's contact area once it landed, so the rock reads the same supporter, only lower: it is judged again and pulled
    /// down onto the floor too.
    /// </summary>
    [Fact]
    public void AnArrivalWhoseSupporterLandsALittleLowerIsJudgedAgain()
    {
        var bowlNow = RockNow + new Vector3(0, 0, PlateThickness);
        var scene = new Scene
        {
            RockBase = Plate.Base, SecondRockAt = bowlNow, SecondRockBase = Bowl.Base, ThirdRockAt = bowlNow + new Vector3(0, 0, BowlFloor),
        };

        var checks = scene.Check(goingBack: [SecondRockCandidate, ThirdRockCandidate]);

        var pulled = checks.Written.Others.Select(other => other.Outcome).ToList();
        Assert.Equal([(RockKey(1, BaseGameMod), OtherModsObjectFate.PulledDown), (RockKey(2, BaseGameMod), OtherModsObjectFate.PulledDown)], pulled.Select(outcome => (outcome.Object.Key, outcome.Fate)));
        Assert.Equal(bowlNow.Z - PlateThickness, pulled[0].NewPosition.Z, tolerance: 0.5f);
        Assert.Equal(bowlNow.Z - PlateThickness + BowlFloor, pulled[1].NewPosition.Z, tolerance: 0.5f);
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit);
    }

    /// <summary>Stacks of three rocks like the one above side by side, some of their rocks of an unrelated mod: the same with one worker as with eight.</summary>
    [Fact]
    public void ArrivalsThatDoNotFitAreTheSameWhateverTheNumberOfWorkers()
    {
        var stacks = ManyStacks();
        HashSet<int> goingBack = [.. Enumerable.Range(0, stacks.Rocks.Count).Where(rock => rock % StackHeight != 0)];

        var one = stacks.CheckOneAfterAnother(threads: 1, oneFollowUpForAll: true, [goingBack])[0];
        var eight = stacks.CheckOneAfterAnother(threads: 8, oneFollowUpForAll: true, [goingBack])[0];

        Assert.Equal(one.Written.Others.Select(other => other.Outcome), eight.Written.Others.Select(other => other.Outcome));
        Assert.Equal(WrittenOthers(one), WrittenOthers(eight));
        Assert.Equal(one.ArrivingWhereTheyDoNotFit.Keys, eight.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Equal(
            one.ArrivingWhereTheyDoNotFit.Values.Select(refused => (refused.Misfit, string.Join(", ", refused.Causes))),
            eight.ArrivingWhereTheyDoNotFit.Values.Select(refused => (refused.Misfit, string.Join(", ", refused.Causes))));
        Assert.NotEmpty(one.Written.Others);
        Assert.NotEmpty(one.ArrivingWhereTheyDoNotFit);
    }

    /// <summary>
    /// Before the cleaned mod the second rock lay on a rock that an unrelated mod has since moved away: nothing stands under the second
    /// rock's old place without the cleaned mod either. The second rock goes back as it stood then, and nothing is refused, pulled down
    /// or removed for the unrelated mod's change (decision 33).
    /// </summary>
    [Fact]
    public void ACandidateWhoseSupportAnUnrelatedModMovedAwayStillGoesBack()
    {
        var movedAway = Placed(LaterMod, BesideIndex, Block, new Vector3(2500, 2500, HalfSize));
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top), RockTurnedBack = true, Others = [movedAway] };

        var checks = scene.Check(goingBack: [SecondRockCandidate]);

        AssertNothingFollowed(checks);
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// The second rock stood on the rock, or on the crate, 1000 units north; both were moved, still stacked, to where they stand now.
    /// The support is no candidate (its area does not let it go back), so it stays; only the second rock goes back. When the cleaned mod
    /// moved the support, the second rock would float at its old place: a base game rock is pulled down onto the ground, started by
    /// itself, and an unrelated mod's rock, which the follow-up may not change, stays where the cleaned mod put it. When an unrelated mod
    /// moved the support, the second rock goes back as it stood before the cleaned mod and nothing is followed (decision 33).
    /// </summary>
    [Theory]
    [InlineData(RockCandidate, true, "Base.esm", true, false)]
    [InlineData(RockCandidate, true, "Later.esp", false, true)]
    [InlineData(CrateCandidate, true, "Base.esm", true, false)]
    [InlineData(CrateCandidate, true, "Later.esp", false, true)]
    [InlineData(RockCandidate, false, "Base.esm", false, false)]
    public void ACandidateGoingBackOntoASupportTheCleanedModMovedThatStaysNeverFloats(int support, bool movedByCleanedMod, string secondRockPlugin, bool pulled, bool stays)
    {
        var secondRockMod = ModKey.FromNameAndExtension(secondRockPlugin);
        var supportNow = support == RockCandidate ? RockNow : CrateNow;
        var scene = new Scene { SecondRockAt = supportNow + new Vector3(0, 0, Top), SecondRockMod = secondRockMod, NoCandidate = support, NoCandidateMovedByCleanedMod = movedByCleanedMod };
        const int SecondRockAmongCleared = SecondRockCandidate - 1;

        var checks = scene.Check(goingBack: [SecondRockAmongCleared]);

        var secondRock = RockKey(1, secondRockMod);
        Assert.Equal(
            pulled ? [(secondRock, secondRock.ToString(), OtherModsObjectFate.PulledDown)] : [],
            checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy), other.Outcome.Fate)));
        Assert.Equal(stays ? [SecondRockAmongCleared] : [], checks.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Empty(checks.Written.Targets);
    }

    /// <summary>The ways the rock under the second rock's old place is missing, besides being moved by the cleaned mod.</summary>
    public enum SupportMissing { DisabledByCleanedMod, DeletedByCleanedMod, RemovedByRun, PulledAwayByRun }

    /// <summary>
    /// Before the cleaned mod the second rock lay on the rock. The rock is no candidate, and it is missing there: the cleaned mod
    /// disabled or deleted it (after moving it along with the second rock), or the run removed it or pulled it down from under the
    /// second rock's old place (the cleaned mod never changed it). The second rock goes back and would float, so it is pulled down
    /// there, started by itself, as every arrival without support is (decision 33).
    /// </summary>
    [Theory]
    [InlineData(SupportMissing.DisabledByCleanedMod)]
    [InlineData(SupportMissing.DeletedByCleanedMod)]
    [InlineData(SupportMissing.RemovedByRun)]
    [InlineData(SupportMissing.PulledAwayByRun)]
    public void ACandidateGoingBackOntoAMissingSupportNeverFloats(SupportMissing missing)
    {
        var byCleanedMod = missing is SupportMissing.DisabledByCleanedMod or SupportMissing.DeletedByCleanedMod;
        var scene = new Scene
        {
            SecondRockAt = RockNow + new Vector3(0, byCleanedMod ? 0 : -1000, Top),
            NoCandidate = RockCandidate,
            NoCandidateMovedByCleanedMod = byCleanedMod,
            RockHidden = byCleanedMod,
            RockDeleted = missing == SupportMissing.DeletedByCleanedMod,
            TakenAway = missing switch
            {
                SupportMissing.RemovedByRun => new Dictionary<OtherId, OtherObject?> { [new OtherId(0)] = null },
                SupportMissing.PulledAwayByRun => new Dictionary<OtherId, OtherObject?> { [new OtherId(0)] = RockObject(0, RockNow - new Vector3(0, 0, Top)) },
                _ => NothingTakenAway,
            },
        };
        const int SecondRockAmongCleared = SecondRockCandidate - 1;

        var checks = scene.Check(goingBack: [SecondRockAmongCleared]);

        var secondRock = RockKey(1, BaseGameMod);
        Assert.Equal(
            [(secondRock, secondRock.ToString(), OtherModsObjectFate.PulledDown)],
            checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy), other.Outcome.Fate)));
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// Before the cleaned mod the second rock lay on the rock; the cleaned mod disabled the rock and a later mod moved it far away, so
    /// the world without the cleaned mod has it there, not under the second rock's old place. The second rock goes back as it stood
    /// then, and nothing is pulled down, removed or refused for the later mod's move (decision 33).
    /// </summary>
    [Fact]
    public void AHiddenSupportALaterModMovedCountsWhereThatModPutIt()
    {
        var scene = new Scene
        {
            SecondRockAt = RockNow + new Vector3(0, 0, Top),
            NoCandidate = RockCandidate,
            RockHidden = true,
            RockPositionKeptByLater = true,
        };
        const int SecondRockAmongCleared = SecondRockCandidate - 1;

        var checks = scene.Check(goingBack: [SecondRockAmongCleared]);

        AssertNothingFollowed(checks);
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// The second rock lay on the rock 1000 units north as a lantern, its origin at its foot; the cleaned mod moved both and gave the
    /// second rock a rock's model, its origin at its middle, which reaches into the rock where it stands now. The rock
    /// stays; the second rock goes back to its place and its lantern model there, where it would float: a base game one is pulled down
    /// onto the ground, started by itself, and an unrelated mod's one, which the follow-up may not change, stays where the cleaned mod
    /// put it. With the rock going back too, it lands on it again and nothing is followed.
    /// </summary>
    [Theory]
    [InlineData(true, "Base.esm", false, false)]
    [InlineData(false, "Base.esm", true, false)]
    [InlineData(false, "Later.esp", false, true)]
    public void ACandidateGoingBackInAnotherShapeNeverFloats(bool rockGoesBack, string secondRockPlugin, bool pulled, bool stays)
    {
        var secondRockMod = ModKey.FromNameAndExtension(secondRockPlugin);
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, HalfSize), SecondRockMod = secondRockMod, SecondRockBaseBack = Lantern.Base };

        var checks = scene.Check(goingBack: rockGoesBack ? [RockCandidate, SecondRockCandidate] : [SecondRockCandidate]);

        AssertArrivalOfTheSecondRock(checks, RockKey(1, secondRockMod), pulled, stays, SecondRockCandidate);
        if (pulled) Assert.Equal(Lantern.Base, checks.Written.Others[0].Outcome.Object.Base);
    }

    /// <summary>
    /// The second rock lay on the rock, which the cleaned mod gave a model without a mesh in place; the cleaned mod moved the second
    /// rock away. The rock keeps that model, as a candidate that stays or as no candidate, so the second rock would float where it goes
    /// back: a base game one is pulled down, started by itself, and an unrelated mod's one stays where the cleaned mod put it.
    /// </summary>
    [Theory]
    [InlineData(true, "Base.esm", true, false)]
    [InlineData(true, "Later.esp", false, true)]
    [InlineData(false, "Base.esm", true, false)]
    public void ACandidateGoingBackOntoASupportThatKeepsAnotherModelNeverFloats(bool rockIsCandidate, string secondRockPlugin, bool pulled, bool stays)
    {
        var secondRockMod = ModKey.FromNameAndExtension(secondRockPlugin);
        var scene = new Scene
        {
            SecondRockAt = RockNow + new Vector3(0, -1000, Top),
            SecondRockMod = secondRockMod,
            RockBase = WithoutMesh,
            RockBaseBack = Block.Base,
            NoCandidate = rockIsCandidate ? null : RockCandidate,
        };
        var secondRock = rockIsCandidate ? SecondRockCandidate : SecondRockCandidate - 1;

        var checks = scene.Check(goingBack: [secondRock]);

        AssertArrivalOfTheSecondRock(checks, RockKey(1, secondRockMod), pulled, stays, secondRock);
    }

    /// <summary>
    /// The second rock lay on the rock 1000 units north; the cleaned mod moved both and disabled the second rock. The rock stays; the
    /// second rock is shown again at its old place, where it would float: a base game one is pulled down onto the ground, started by
    /// itself, and an unrelated mod's one stays where the cleaned mod put it, disabled.
    /// </summary>
    [Theory]
    [InlineData("Base.esm", true, false)]
    [InlineData("Later.esp", false, true)]
    public void ACandidateShownAgainNeverFloats(string secondRockPlugin, bool pulled, bool stays)
    {
        var secondRockMod = ModKey.FromNameAndExtension(secondRockPlugin);
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top), SecondRockMod = secondRockMod, SecondRockHidden = true };

        var checks = scene.Check(goingBack: [SecondRockCandidate]);

        AssertArrivalOfTheSecondRock(checks, RockKey(1, secondRockMod), pulled, stays, SecondRockCandidate);
        if (pulled) Assert.Equal(0f, checks.Written.Others[0].Outcome.NewPosition.Z - HalfSize, tolerance: 0.5f);
    }

    /// <summary>
    /// The rock stood as a lantern, its foot on the crate 1000 units north of where the crate stands now; the cleaned mod moved the
    /// crate away and gave the rock a rock's model in place. The rock gets its lantern model back where it stands. With the crate going
    /// back too it rests on it again and nothing is followed; with the crate staying it would float, so the base game rock is pulled
    /// down onto the ground in its lantern model, started by itself.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACandidateGettingItsOldModelBackInPlaceNeverFloats(bool crateGoesBack)
    {
        var scene = new Scene { RockBaseBack = Lantern.Base, CrateAt = RockNow + new Vector3(0, -1000, -HalfSize) };

        var checks = scene.Check(goingBack: crateGoesBack ? [RockCandidate, CrateCandidate] : [RockCandidate]);

        Assert.Equal(crateGoesBack ? [] : [(RockKey(0, BaseGameMod), RockStarter, OtherModsObjectFate.PulledDown)], checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy), other.Outcome.Fate)));
        if (!crateGoesBack)
        {
            Assert.Equal(Lantern.Base, checks.Written.Others[0].Outcome.Object.Base);
            Assert.Equal(0f, checks.Written.Others[0].Outcome.NewPosition.Z, tolerance: 0.5f);
        }
        Assert.Empty(checks.Written.Targets);
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Empty(checks.TakingAlongWithoutSupport);
    }

    /// <summary>
    /// A base game lantern and a pebble of the cleaned mod rest on the rock, which gets back another model of the same shape, in place
    /// or 15 units east, so it still holds both up: nothing is followed. Gone back 1000 units north it leaves them, so the lantern is
    /// pulled down and the pebble goes, both started by the rock.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, false)]
    [InlineData(15f, 0f, false)]
    [InlineData(0f, 1000f, true)]
    public void WhatTheOldModelOfACandidateStillHoldsUpWhereItGoesBackStays(float east, float north, bool followed)
    {
        var lantern = Placed(BaseGameMod, LanternIndex, Lantern, RockNow + new Vector3(10, 10, HalfSize));
        var scene = new Scene
        {
            Pebbles = [RockNow + new Vector3(10, -10, HalfSize)],
            Others = [lantern],
            RockBaseBack = Boulder.Base,
            RockBackTo = RockNow + new Vector3(east, north, 0),
        };

        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(followed ? [(new TargetId(1), RockStarter)] : [], WrittenTargets(checks));
        Assert.Equal(followed ? [(lantern.Key, RockStarter)] : [], WrittenOthers(checks));
        Assert.Empty(checks.ArrivingWhereTheyDoNotFit.Keys);
    }

    /// <summary>
    /// The second rock stood on the rock 1000 units north with a map marker, so the game does not draw it; the cleaned mod moved both,
    /// took the marker away and, in one case, disabled the second rock. The rock stays and the second rock goes back with its marker,
    /// where it would float, so the base game rock is pulled down; it holds nothing up wherever it lands. The third rock goes back onto
    /// the ground beside them and is judged again once the second rock landed: it loses nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACandidateGoingBackUndrawnIsPulledDownInTheShapeItGoesBackIn(bool hidden)
    {
        var scene = new Scene { SecondRockAt = RockNow + new Vector3(0, 0, Top), SecondRockHidden = hidden, SecondRockMarkerBack = true, ThirdRockAt = SecondRockNow };

        var checks = scene.Check(goingBack: [SecondRockCandidate, ThirdRockCandidate]);

        var secondRock = RockKey(1, BaseGameMod);
        AssertArrivalOfTheSecondRock(checks, secondRock, pulled: true, stays: false, SecondRockCandidate);
        Assert.True(checks.Written.Others[0].Outcome.Object.HasMapMarker);
    }

    /// <summary>The second rock arriving: pulled down where it arrives, started by itself, or refused, or neither.</summary>
    private static void AssertArrivalOfTheSecondRock(FollowUpChecks checks, RecordKey secondRock, bool pulled, bool stays, int candidate)
    {
        Assert.Equal(
            pulled ? [(secondRock, secondRock.ToString(), OtherModsObjectFate.PulledDown)] : [],
            checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy), other.Outcome.Fate)));
        Assert.Equal(stays ? [candidate] : [], checks.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Empty(checks.Written.Targets);
    }

    /// <summary>
    /// A pebble the cleaned mod moved onto the rock goes with the rock and is restored to where it stood before: on top of the second
    /// rock's old place. With the second rock going back too it lands there; with the second rock staying it would float there, so the
    /// rock that took it along stays.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnObjectTheFollowUpRestoresWithoutSupportKeepsWhatTookItAlong(bool secondRockGoesBack, bool kept)
    {
        var scene = new Scene
        {
            Pebbles = [RockNow + new Vector3(0, 0, HalfSize)],
            PebblesRestoredTo = new Dictionary<int, Vector3> { [0] = SecondRockNow + new Vector3(0, 1000, HalfSize) },
        };

        var checks = scene.Check(goingBack: secondRockGoesBack ? [RockCandidate, SecondRockCandidate] : [RockCandidate]);

        var pebble = scene.Targets()[1];
        Assert.Equal([(pebble.Id, RockStarter)], WrittenTargets(checks));
        Assert.Equal(pebble.Key, Assert.Single(checks.TargetsRestored).Restored.Key);
        Assert.Equal(kept ? [KeyValuePair.Create(RockCandidate, pebble.Key)] : [], checks.TakingAlongWithoutSupport);
    }

    /// <summary>
    /// A base game object without a mesh, such as a marker, that the cleaned mod moved goes back from beside a pebble of the cleaned
    /// mod that reaches its place: it holds up nothing, so nothing goes with it.
    /// </summary>
    [Fact]
    public void ACandidateWithoutAMeshStartsNothing()
    {
        var scene = new Scene { Pebbles = [RockNow + new Vector3(0, 0, -2)], RockBase = WithoutMesh };

        var checks = scene.Check(goingBack: [RockCandidate]);

        AssertNothingFollowed(checks);
    }

    /// <summary>
    /// A base game lantern on the rock that the run removed is gone, so the rock going back takes nothing along; one the run pulled
    /// down onto the rock from above rests on it now, so it is pulled down again, from where the run left it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnObjectTheRunTookAwayIsJudgedWhereTheRunLeftIt(bool pulledDown)
    {
        var onTheRock = RockNow + new Vector3(0, 0, HalfSize);
        var lantern = Placed(BaseGameMod, LanternIndex, Lantern, pulledDown ? onTheRock + new Vector3(0, 0, 100) : onTheRock);
        var scene = new Scene
        {
            Others = [lantern],
            TakenAway = new Dictionary<OtherId, OtherObject?> { [FirstOther] = pulledDown ? lantern with { Id = FirstOther, Position = onTheRock } : null },
        };

        var checks = scene.Check(goingBack: [RockCandidate]);

        Assert.Equal(pulledDown ? [(lantern.Key, RockStarter)] : [], WrittenOthers(checks));
        Assert.All(checks.Written.Others, other => Assert.Equal(onTheRock, other.Outcome.Object.Position));
    }

    [Fact]
    public void TheFollowUpGivesTheSameResultsWhateverTheNumberOfWorkers()
    {
        var one = ManyRocks().Check(threads: 1);
        var eight = ManyRocks().Check(threads: 8);

        Assert.Equal(WrittenTargets(one), WrittenTargets(eight));
        Assert.Equal(WrittenOthers(one), WrittenOthers(eight));
        Assert.Equal(one.Written.Others.Select(other => other.Outcome), eight.Written.Others.Select(other => other.Outcome));
        Assert.NotEmpty(one.Written.Targets);
        Assert.NotEmpty(one.Written.Others);
    }

    /// <summary>
    /// The passes ask one follow-up about ever fewer rocks going back, and it keeps the weights before it measured between them: each
    /// answer is the one a new follow-up gives, with one worker and with four.
    /// </summary>
    [Fact]
    public void AFollowUpAskedPassAfterPassAnswersAsANewOneEachTimeWhateverTheNumberOfWorkers()
    {
        List<HashSet<int>> passes = [[.. Enumerable.Range(0, 30)], [.. Enumerable.Range(0, 30).Where(rock => rock % 2 == 0)], [.. Enumerable.Range(0, 10)]];

        var fresh = ManyRocks().CheckOneAfterAnother(threads: 1, oneFollowUpForAll: false, passes);
        Assert.All(
            [ManyRocks().CheckOneAfterAnother(threads: 1, oneFollowUpForAll: true, passes), ManyRocks().CheckOneAfterAnother(threads: 4, oneFollowUpForAll: true, passes)],
            shared =>
            {
                Assert.Equal(fresh.Select(WrittenTargets), shared.Select(WrittenTargets));
                Assert.Equal(fresh.Select(WrittenOthers), shared.Select(WrittenOthers));
                Assert.Equal(fresh.SelectMany(checks => checks.Written.Others.Select(other => other.Outcome)), shared.SelectMany(checks => checks.Written.Others.Select(other => other.Outcome)));
            });
        Assert.All(fresh, checks => Assert.NotEmpty(checks.Written.Others));
    }

    /// <summary>The rock's key, as a list of the placed records that started a follow-up.</summary>
    private static readonly string RockStarter = RockKey(0, BaseGameMod).ToString();

    /// <summary>Each target object the follow-up writes, with the placed records that started it.</summary>
    private static List<(TargetId Target, string StartedBy)> WrittenTargets(FollowUpChecks checks) =>
        [.. checks.Written.Targets.Select(followed => (followed.Target, string.Join(", ", followed.StartedBy)))];

    /// <summary>Each object of any plugin the follow-up writes, with the placed records that started it.</summary>
    private static List<(RecordKey Object, string StartedBy)> WrittenOthers(FollowUpChecks checks) =>
        [.. checks.Written.Others.Select(other => (other.Outcome.Object.Key, string.Join(", ", other.StartedBy)))];

    private static void AssertNothingFollowed(FollowUpChecks checks)
    {
        Assert.Empty(checks.Written.Targets);
        Assert.Empty(checks.Written.Others);
        Assert.Empty(checks.CopiesLost);
        Assert.Empty(checks.HoldingUp);
    }

    /// <summary>
    /// Thirty rocks, every one leaving; every second carries a pebble of the cleaned mod, every third a base game lantern with a pebble
    /// on it, every fifth an unrelated mod's lantern with a pebble on it.
    /// </summary>
    private static ManyRockScene ManyRocks()
    {
        const int Count = 30;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [];
        List<Vector3> pebbles = [];
        for (var index = 0; index < Count; index++)
        {
            var now = new Vector3(100 + index * 100, 2000, HalfSize);
            rocks.Add(Rock(index, now));
            others.Add(RockObject(index, now));
            if (index % 2 == 0) pebbles.Add(now + new Vector3(-10, -10, HalfSize));
            if (index % 3 == 0)
            {
                others.Add(Placed(BaseGameMod, Count + index, Lantern, now + new Vector3(10, 10, HalfSize)));
                pebbles.Add(now + new Vector3(10, 10, HalfSize + 6));
            }
            if (index % 5 == 0)
            {
                others.Add(Placed(LaterMod, 2 * Count + index, Lantern, now + new Vector3(-10, 10, HalfSize)));
                pebbles.Add(now + new Vector3(-10, 10, HalfSize + 6));
            }
        }
        return new ManyRockScene(rocks, others, pebbles);
    }

    private const int StackHeight = 3;

    /// <summary>
    /// Eight stacks of three rocks the cleaned mod moved, still stacked, from 1000 units north: bottom, middle and top, in that index
    /// order. Every fourth middle rock, from the second stack on, and every third top rock are of an unrelated mod.
    /// </summary>
    private static ManyRockScene ManyStacks()
    {
        const int Count = 8;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [];
        for (var stack = 0; stack < Count; stack++)
        {
            for (var level = 0; level < StackHeight; level++)
            {
                var index = stack * StackHeight + level;
                var mod = (level == 1 && stack % 4 == 1) || (level == 2 && stack % 3 == 0) ? LaterMod : BaseGameMod;
                var now = new Vector3(100 + stack * 200, 3000, HalfSize + level * Top);
                rocks.Add(Rock(index, now, mod));
                others.Add(RockObject(index, now, mod));
            }
        }
        return new ManyRockScene(rocks, others, []);
    }

    /// <summary>Rocks of the base game the cleaned mod moved, and objects resting on them.</summary>
    private sealed record ManyRockScene(List<RecordChangedByCleanedMod> Rocks, List<OtherObject> Others, List<Vector3> Pebbles)
    {
        public FollowUpChecks Check(int threads) => CheckOneAfterAnother(threads, oneFollowUpForAll: true, [[.. Enumerable.Range(0, Rocks.Count)]])[0];

        /// <summary>The follow-up of each set of rocks going back, in order: asked of one follow-up, as the passes ask it, or of a new one each time.</summary>
        public List<FollowUpChecks> CheckOneAfterAnother(int threads, bool oneFollowUpForAll, IReadOnlyList<HashSet<int>> goingBackSets)
        {
            ImmutableArray<TargetObject> targets = [.. Pebbles.Select((place, index) => PebbleTarget(index, place))];
            var collected = TestScenes.CreateWorld(targets, [.. Others.Select((other, index) => other with { Id = new OtherId(index) })]) with { ChangedRecords = [.. Rocks] };
            FollowUpOfGoingBack Create() => new(RuleOver(collected, threads, Support()), DecisionsOver(targets), NothingTakenAway, Rocks, collected, landUnder: (_, _) => null);
            var shared = Create();
            return
            [
                .. goingBackSets.Select(goingBack =>
                    (oneFollowUpForAll ? shared : Create()).Check(goingBack, new HashSet<int>(), PassOf(collected, Rocks, goingBack), land: null)),
            ];
        }
    }

    /// <summary>
    /// The rock at <see cref="RockNow"/>, object 0; the crate, target 0; the second rock, object after the others; pebbles of the
    /// cleaned mod at the given places, the targets after the crate; the other objects in between, numbered from 1.
    /// </summary>
    private sealed record Scene
    {
        public List<Vector3> Pebbles { get; init; } = [];
        public List<OtherObject> Others { get; init; } = [];
        public Vector3 CrateAt { get; init; } = CrateNow;
        public Vector3 SecondRockAt { get; init; } = SecondRockNow;
        public ModKey SecondRockMod { get; init; } = BaseGameMod;
        public BaseKey SecondRockBase { get; init; } = Block.Base;

        /// <summary>Where the cleaned mod put a third rock, the fourth candidate, moved from 1000 units north; none when null.</summary>
        public Vector3? ThirdRockAt { get; init; }

        public ModKey ThirdRockMod { get; init; } = BaseGameMod;

        /// <summary>The pebbles whose removal restores them instead, to the given place, by their index among the pebbles.</summary>
        public Dictionary<int, Vector3> PebblesRestoredTo { get; init; } = [];

        public BaseKey RockBase { get; init; } = Block.Base;
        public OtherModsObjectsHandling Handling { get; init; } = OtherModsObjectsHandling.PullObjectToTheGround;
        public AlsoRemoveSettings AlsoRemove { get; init; } = TestPlacedGuardRules.DefaultFollowUp;

        /// <summary>The rock goes back only to its earlier turn instead of its earlier place.</summary>
        public bool RockTurnedBack { get; init; }

        /// <summary>The base the rock goes back to with its base and data, and nothing else unless <see cref="RockBackTo"/> is set; null when its position goes back.</summary>
        public BaseKey? RockBaseBack { get; init; }

        /// <summary>Where the rock goes back to, with <see cref="RockBaseBack"/> too when it is set; 1000 units north when null.</summary>
        public Vector3? RockBackTo { get; init; }

        /// <summary>The candidate whose only part going back is its enable state, which disables it; null for none.</summary>
        public int? HiddenAgain { get; init; }

        public IReadOnlyDictionary<OtherId, OtherObject?> TakenAway { get; init; } = NothingTakenAway;

        public ImmutableArray<TargetObject> Targets() =>
            [TestTargets.Create(CrateTarget, TestTargets.At(CrateAt), Block.Base, TestTargets.Space), .. Pebbles.Select((place, index) => PebbleTarget(index + 1, place))];

        public OtherObject Rock() => RockObject(0, RockNow) with { Base = RockBase };

        /// <summary>The rock leaving to its earlier place.</summary>
        public ObjectsLeaving RockLeaving() => new([new OtherLeaving(Rock(), Candidates()[RockCandidate].Earlier.Placement.Transform)], [], [], [], [], [], Land: null);

        public List<OtherObject> Objects() =>
        [
            .. (RockHidden ? Array.Empty<OtherObject>() : [Rock()]).Concat(Others)
                .Concat(SecondRockHidden ? Array.Empty<OtherObject>() : [RockObject(1, SecondRockAt, SecondRockMod) with { Base = SecondRockBase }])
                .Concat(ThirdRockAt is { } third ? [RockObject(2, third, ThirdRockMod)] : Array.Empty<OtherObject>())
                .Select((other, index) => other with { Id = new OtherId(index) }),
        ];

        /// <summary>The candidates: the rock, the crate, the second rock and the third one, each moved from 1000 units north unless set otherwise.</summary>
        public List<RecordChangedByCleanedMod> Candidates() =>
        [
            ShownBeforeOrNot(HiddenOrNot(RockCandidate, DataBackOrNot(RockChange())) with { BaseNow = RockBase }),
            HiddenOrNot(CrateCandidate, Moved(TestTargets.Key(CrateTarget), CrateAt) with { Target = new TargetId(CrateTarget) }),
            SecondRockChanged(FollowUpOfGoingBackTests.Rock(1, SecondRockAt, SecondRockMod)),
            .. ThirdRockAt is { } third ? [FollowUpOfGoingBackTests.Rock(2, third, ThirdRockMod)] : Array.Empty<RecordChangedByCleanedMod>(),
        ];

        /// <summary>The rock or the crate, left out of the candidates, as when its area does not let it go back; none when null.</summary>
        /// <remarks>The candidates after it come one index earlier among those whose areas let them go back.</remarks>
        public int? NoCandidate { get; init; }

        /// <summary>Whether the cleaned mod moved <see cref="NoCandidate"/>; when not, an unrelated mod moved it.</summary>
        public bool NoCandidateMovedByCleanedMod { get; init; } = true;

        /// <summary>The cleaned mod disabled the rock where it moved it, so the game does not show it; it was shown before.</summary>
        public bool RockHidden { get; init; }

        /// <summary>With <see cref="RockHidden"/>: the cleaned mod deleted the rock instead, so its winning version has no placement.</summary>
        public bool RockDeleted { get; init; }

        /// <summary>With <see cref="RockHidden"/>: a later mod moved the rock, so its position stays as that mod set it.</summary>
        public bool RockPositionKeptByLater { get; init; }

        /// <summary>The base the second rock goes back to with its base and data, besides its position; none when null.</summary>
        public BaseKey? SecondRockBaseBack { get; init; }

        /// <summary>The cleaned mod disabled the second rock where it moved it, so the game shows it again when it goes back.</summary>
        public bool SecondRockHidden { get; init; }

        /// <summary>The second rock goes back with its base and data, which carry a map marker, so the game no longer draws it.</summary>
        public bool SecondRockMarkerBack { get; init; }

        /// <summary>The candidates whose areas let them go back.</summary>
        public List<RecordChangedByCleanedMod> Cleared() => [.. Candidates().Where((_, index) => index != NoCandidate)];

        public CollectedObjects World() =>
            TestScenes.CreateWorld(Targets(), Objects()) with { ChangedRecords = [.. NoCandidateMovedByCleanedMod ? Candidates() : Cleared()] };

        public IRemovalDecisions Decisions() =>
            DecisionsOver(Targets(), [.. Targets().Select((target, index) => index > 0 && PebblesRestoredTo.TryGetValue(index - 1, out var place) ? place : (Vector3?)null)]);

        public IFollowUpOfObjectsLeaving Rule(int threads) => RuleOver(World(), threads, Support(Handling), AlsoRemove);

        /// <param name="goingBack">By their index among <see cref="Cleared"/>.</param>
        /// <param name="rule">The follow-up asked; the run's rule over the scene when null.</param>
        public FollowUpChecks Check(int[] goingBack, int[]? withClone = null, IFollowUpOfObjectsLeaving? rule = null)
        {
            var world = World();
            var candidates = Cleared();
            var going = goingBack.ToHashSet();
            return new FollowUpOfGoingBack(rule ?? RuleOver(world, threads: 1, Support(Handling), AlsoRemove), Decisions(), TakenAway, candidates, world, landUnder: (_, _) => null)
                .Check(going, (withClone ?? []).ToHashSet(), PassOf(world, candidates, going), land: null);
        }

        private RecordChangedByCleanedMod RockChange()
        {
            if (RockTurnedBack) return Turned(RockKey(0, BaseGameMod), RockNow);
            return RockBackTo is { } earlier
                ? Changed(RockKey(0, BaseGameMod), RockNow, new EulerPlacement(earlier, Vector3.Zero, 1f), PlacedParts.Position)
                : FollowUpOfGoingBackTests.Rock(0, RockNow);
        }

        private RecordChangedByCleanedMod DataBackOrNot(RecordChangedByCleanedMod change) =>
            RockBaseBack is { } earlierBase
                ? change with { Earlier = change.Earlier with { Base = earlierBase, Undo = RockBackTo is null ? PlacedParts.BaseAndData : change.Earlier.Undo | PlacedParts.BaseAndData } }
                : change;

        private RecordChangedByCleanedMod ShownBeforeOrNot(RecordChangedByCleanedMod change) =>
            RockHidden
                ? change with { Earlier = PositionKeptOrNot(change.Earlier with { Undo = change.Earlier.Undo | PlacedParts.Presence }), WhereItIsNow = RockDeleted ? null : change.WhereItIsNow }
                : change;

        private EarlierVersion PositionKeptOrNot(EarlierVersion earlier) =>
            RockPositionKeptByLater
                ? earlier with { Undo = earlier.Undo & ~PlacedParts.Position, LeftToLaterPlugins = earlier.LeftToLaterPlugins.Add(PlacedParts.Position, LaterMod.ToPluginName()) }
                : earlier;

        private RecordChangedByCleanedMod SecondRockChanged(RecordChangedByCleanedMod change)
        {
            var earlier = change.Earlier;
            if (SecondRockBaseBack is { } earlierBase) earlier = earlier with { Base = earlierBase, Undo = earlier.Undo | PlacedParts.BaseAndData };
            if (SecondRockHidden) earlier = earlier with { Undo = earlier.Undo | PlacedParts.Presence };
            if (SecondRockMarkerBack) earlier = earlier with { Reference = earlier.Reference with { HasMapMarker = true }, Undo = earlier.Undo | PlacedParts.BaseAndData };
            return change with { Earlier = earlier, BaseNow = SecondRockBase };
        }

        private RecordChangedByCleanedMod HiddenOrNot(int candidate, RecordChangedByCleanedMod change) =>
            HiddenAgain == candidate
                ? change with { Earlier = change.Earlier with { Undo = PlacedParts.Presence }, PresenceBefore = new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false) }
                : change;
    }

    private static readonly IReadOnlyDictionary<OtherId, OtherObject?> NothingTakenAway = new Dictionary<OtherId, OtherObject?>();

    private static OtherModsObjectsSettings Support(OtherModsObjectsHandling handling = OtherModsObjectsHandling.PullObjectToTheGround) =>
        new(handling, TouchGap: 1f, SupportLostFraction: 0.5f);

    private static WorldStanding Standing(IRemovalDecisions final) => new(final, NothingTakenAway);

    /// <summary>Whether a placed record ends removed with the given candidates going back and none removed by the run.</summary>
    private static PresenceInPass PassOf(CollectedObjects world, List<RecordChangedByCleanedMod> candidates, HashSet<int> goingBack) =>
        new(FinalPresence.Of(world, new HashSet<RecordKey>(), candidates), goingBack.Contains);

    /// <summary>The run's decisions over the target objects: none decided, none protected.</summary>
    /// <param name="restoredTo">By target: where its removal restores it to instead of disabling it; null to disable it.</param>
    private static IRemovalDecisions DecisionsOver(ImmutableArray<TargetObject> targets, ImmutableArray<Vector3?> restoredTo = default) =>
        RemovalDecisions.Start(
            ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved),
            [
                .. targets.Select((target, index) => !restoredTo.IsDefault && restoredTo[index] is { } place
                    ? new WhenRemoved.Restore(TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(place, Vector3.Zero, 1f), target.Base))
                    : (WhenRemoved)new WhenRemoved.Disable()),
            ]);

    private static IFollowUpOfObjectsLeaving RuleOver(CollectedObjects world, int threads, OtherModsObjectsSettings support, AlsoRemoveSettings? alsoRemove = null) =>
        TestPlacedGuardRules.Create(Shapes, Trust, Plugins, support, TestGround.FlatAtZero(), threads).FollowUpOver(world, alsoRemove ?? TestPlacedGuardRules.DefaultFollowUp);

    /// <summary>The crate restored to its earlier version on a copy of <paramref name="final"/>.</summary>
    private static IRemovalDecisions GoingBack(IRemovalDecisions final, Scene scene) =>
        final.WorkingCopy().RecordGoingBack([new GoingBack(new TargetId(CrateTarget), new GoingBackWhereRemoved(), scene.Candidates()[CrateCandidate].Earlier)]);

    private static TargetObject PebbleTarget(int index, Vector3 place) => TestTargets.Create(index, TestTargets.At(place), Pebble.Base, TestTargets.Space);

    private static OtherObject Placed(ModKey mod, int index, TestStatic model, Vector3 position) => TestShapes.Placed(mod, index, model.Base, position);

    /// <summary>A rock where the cleaned mod put it.</summary>
    private static OtherObject RockObject(int index, Vector3 now, ModKey? mod = null) =>
        TestShapes.Placed(mod ?? BaseGameMod, index, Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, (mod ?? BaseGameMod).ToPluginName()) };

    private static RecordKey RockKey(int index, ModKey mod) => new FormKey(mod, 0x900 + (uint)index).ToRecordKey();

    private static RecordChangedByCleanedMod Rock(int index, Vector3 now, ModKey? mod = null) => Moved(RockKey(index, mod ?? BaseGameMod), now);

    /// <summary>A block the cleaned mod moved to <paramref name="now"/> from 1000 units north, whose position goes back.</summary>
    private static RecordChangedByCleanedMod Moved(RecordKey key, Vector3 now) => Changed(key, now, new EulerPlacement(now + new Vector3(0, 1000, 0), Vector3.Zero, 1f), PlacedParts.Position);

    /// <summary>A block the cleaned mod turned about its vertical axis at <paramref name="now"/>, whose rotation goes back.</summary>
    private static RecordChangedByCleanedMod Turned(RecordKey key, Vector3 now) => Changed(key, now, new EulerPlacement(now, new Vector3(0, 0, 0.05f), 1f), PlacedParts.Rotation);

    private static RecordChangedByCleanedMod Changed(RecordKey key, Vector3 now, EulerPlacement earlier, PlacedParts undo) =>
        new(
            key,
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            Block.Base,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, earlier, Block.Base) with { Undo = undo },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);
}
