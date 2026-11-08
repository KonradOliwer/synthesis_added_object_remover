using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

using static FollowUpScenes;

/// <summary>
/// What the removal steps' follow-up finds resting on the candidates of the restore leaving where they stand now: which objects it
/// follows up by plugin and trust, when a candidate going back starts it, and what happens to copies and to what is held up.
/// </summary>
public sealed class FollowUpOfGoingBackTests
{
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
}
