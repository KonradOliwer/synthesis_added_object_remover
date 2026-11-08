using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static FollowUpScenes;

/// <summary>What the removal steps' follow-up does with the candidates of the restore where they arrive: pulled down, refused or left, when what held them up stays behind.</summary>
public sealed class FollowUpOfGoingBackArrivalTests
{
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
}
