using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedGuardScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The cleaned mod's objects that stay beside the rocks going back, the follow-up their going back starts and the rounds it takes,
/// over in-memory scenes.
/// </summary>
public sealed class RestoreWhereRemovedGuardFollowUpTests
{
    /// <summary>Two pebbles of the cleaned mod stay beside the removed house: far under the limit by size, two of three by number.</summary>
    [Fact]
    public void TwoSmallPiecesThatStayKeepTheAreaByNumberAlone()
    {
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleAt(1, new Vector3(300, -300, 0)), PebbleAt(2, new Vector3(-300, -300, 0))],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((false, AreaVerdict.StillStanding), (judged.IsRestored, judged.JudgedPlace!.Verdict));
        Assert.True(judged.JudgedPlace.CountPercent > MaxStandingCountPercent && judged.JudgedPlace.SizePercent < MaxStandingSizePercent);
    }

    /// <summary>The house stays and two pebbles beside it were removed: one of three by number, nearly all by size.</summary>
    [Fact]
    public void OneLargePieceThatStaysKeepsTheAreaBySizeAlone()
    {
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleAt(1, new Vector3(300, -300, 0)), PebbleAt(2, new Vector3(-300, -300, 0))],
            Removed = [1, 2],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((false, AreaVerdict.StillStanding), (judged.IsRestored, judged.JudgedPlace!.Verdict));
        Assert.True(judged.JudgedPlace.CountPercent < MaxStandingCountPercent && judged.JudgedPlace.SizePercent > MaxStandingSizePercent);
    }

    /// <summary>A pebble of the cleaned mod's run that stays, too small to keep the area from being cleared, lies next to the rock's old place.</summary>
    [Fact]
    public void AnObjectOfTheCleanedModThatStaysRefusesARockGoingBackNextToIt()
    {
        var pebble = TestTargets.Create(1, TestTargets.At(OldPlace + new Vector3(ZoneReach, 0, 0)), Pebble.Base, TestTargets.Space);
        var scene = new Scene { Targets = [RemovedHouse(), pebble], Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, pebble.Key, Failure: null), judged.Refused);
    }

    /// <summary>
    /// A pebble of the cleaned mod stays 70 units from the rock's old place, beyond the zones of the changed objects' settings. Its own
    /// collision group may check 100 units around it. The rock keeps a scale Later.esp set. Whether or not the removal steps believe
    /// Later.esp, the next patcher reads the rock the patch put back as support only, never as a cause, so the rock goes back.
    /// </summary>
    [Theory]
    [InlineData(true, VersionTrust.None)]
    [InlineData(true, VersionTrust.Full)]
    [InlineData(false, VersionTrust.None)]
    public void ARockGoingBackIsNoCauseForAnObjectOfTheCleanedModWhoseOwnGroupReachesIt(bool groupJudgesThePebble, VersionTrust laterTrust)
    {
        var pebble = PebbleAt(1, OldPlace + new Vector3(ZoneReach + 40f, 0, 0));
        var moved = Rock(0, OldPlace, PlaceNow);
        var rock = moved with { Earlier = moved.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Rocks = [rock],
            Others = [RockNow(0, PlaceNow)],
            StandingTooClose = groupJudgesThePebble ? RestoreSettings.TooClose with { DistanceInUnits = 100f } : null,
            LaterTrust = laterTrust,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A pebble of the cleaned mod's run that stays lies on the rock where the cleaned mod put it: the rock goes back and starts the
    /// follow-up of the pebble, which is removed with it, unless the follow-up mode is Nothing or the pebble is protected, which the
    /// follow-up keeps where it stands, as the removal steps do.
    /// </summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, false, true)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, false, true)]
    [InlineData(FollowUpRemovalMode.Nothing, false, false)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, false)]
    public void ARockAnObjectOfTheCleanedModRestsOnGoesBackAndStartsItsFollowUp(FollowUpRemovalMode mode, bool pebbleProtected, bool removed)
    {
        var pebble = TestTargets.Create(1, TestTargets.At(PlaceNow + new Vector3(0, 0, RockHalfSize)), Pebble.Base, TestTargets.Space);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = pebbleProtected ? [] : [1],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
            FollowUp = TestPlacedGuardRules.DefaultFollowUp with { Mode = mode },
        };

        var result = Plan(scene);
        var judged = Assert.Single(result.Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
        Assert.Equal(
            removed ? [(pebble.Id, judged.Change.Key.ToString())] : [],
            result.FollowUp.Targets.Select(followed => (followed.Target, string.Join(", ", followed.StartedBy))));
        Assert.All(result.FollowUp.Targets, followed => Assert.Null(followed.RestoredTo));
    }

    /// <summary>
    /// A pebble of the cleaned mod lies on rock 0 where the cleaned mod put it, and is the only object of the cleaned mod around the
    /// old place of rock 1. In the first round rock 0 goes back and its follow-up removes the pebble, while rock 1 stays for the
    /// pebble standing; the second round judges the areas with the pebble removed, so rock 1 goes back too. With the pebble
    /// protected the follow-up removes nothing, and one round leaves rock 1 where the cleaned mod put it.
    /// </summary>
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void AFollowUpRemovalNearAMovedRockBringsTheRockBackInTheNextRound(bool pebbleProtected, int rounds)
    {
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleOnRockAt(1, PlaceNow)],
            Unprotected = pebbleProtected ? [] : [1],
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, BesidePebbleOnly(PlaceNow), SecondRockNow)],
            Others = [RockNow(0, PlaceNow), RockNow(1, SecondRockNow)],
        };

        var result = Plan(scene);

        Assert.Equal(rounds, result.Rounds);
        Assert.Equal([true, !pebbleProtected], result.Objects.Select(judged => judged.IsRestored));
        Assert.Equal(pebbleProtected ? [] : [new TargetId(1)], result.FollowUp.Targets.Select(followed => followed.Target));
        Assert.Equal(pebbleProtected ? AreaVerdict.StillStanding : AreaVerdict.Cleared, result.Objects[1].JudgedPlace!.Verdict);
    }

    /// <summary>
    /// A chain: rock 0 going back removes the pebble on it, which clears the area of rock 1; rock 1 going back removes a second pebble
    /// on it, which clears the area of rock 2. Each removal brings the next rock back one round later, and the rounds end once a
    /// round's follow-up removes nothing new: three rounds for the two follow-up removals. The same with more workers.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AChainOfFollowUpRemovalsBringsEachRockBackOneRoundLaterAndEnds(int workers)
    {
        var thirdRockNow = SecondRockNow + new Vector3(0, -3 * SearchRadius, 0);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleOnRockAt(1, PlaceNow), PebbleOnRockAt(2, SecondRockNow)],
            Unprotected = [1, 2],
            Rocks =
            [
                Rock(0, OldPlace, PlaceNow),
                Rock(1, BesidePebbleOnly(PlaceNow), SecondRockNow),
                Rock(2, BesidePebbleOnly(SecondRockNow), thirdRockNow),
            ],
            Others = [RockNow(0, PlaceNow), RockNow(1, SecondRockNow), RockNow(2, thirdRockNow)],
            Workers = workers,
        };

        var result = Plan(scene);

        Assert.Equal(3, result.Rounds);
        Assert.All(result.Objects, judged => Assert.True(judged.IsRestored));
        Assert.Equal([new TargetId(1), new TargetId(2)], result.FollowUp.Targets.Select(followed => followed.Target));
    }

    /// <summary>
    /// A rock the cleaned mod moved that is one of its targets goes back, and its follow-up removes the pebble on it. The area where the
    /// rock stands now is judged for the copy rule and holds the pebble and two protected blocks of the cleaned mod: still standing
    /// with the pebble removed, so no decision changes and one round is all. The shares reported count the pebble removed.
    /// </summary>
    [Fact]
    public void AFollowUpRemovalThatChangesNoDecisionLeavesOneRoundAndItsSharesCountIt()
    {
        var rock = TargetRock(2, OldPlace, FarPlaceNow);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleOnRockAt(1, FarPlaceNow), rock.Target, Own(3, FarPlaceNow + new Vector3(300, 0, 0)), Own(4, FarPlaceNow - new Vector3(300, 0, 0))],
            Unprotected = [1, 2],
            Rocks = [rock.Change],
        };

        var result = Plan(scene);

        Assert.Equal(1, result.Rounds);
        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.Equal([new TargetId(1)], result.FollowUp.Targets.Select(followed => followed.Target));
        Assert.Equal((AreaVerdict.StillStanding, 3, 2), (result.Objects[0].WhereItIsNow!.Verdict, result.Objects[0].WhereItIsNow!.Objects, result.Objects[0].WhereItIsNow!.Standing));
    }

    /// <summary>
    /// <see cref="ClashInTheSecondRound"/>: rock 0 would stay and the pebble with it. The earlier round wins: rock 1 is refused, naming
    /// rock 0, rock 0 goes back and the pebble is removed. Rock 2, which the pebble alone kept too, is not to blame and goes back in
    /// the second try. Three rounds: the first, the one undoing it, and its second try.
    /// </summary>
    [Fact]
    public void ARecordANewRoundWouldGoBackIsRefusedWhenItWouldUndoTheEarlierRound()
    {
        var result = Plan(ClashInTheSecondRound());

        Assert.Equal(3, result.Rounds);
        Assert.Equal(
            [(RockKey(0), true, (PlacedRefusal?)null), (RockKey(1), false, new PlacedRefusal(PlacedGuard.EarlierRoundWins, RockKey(0), Failure: null)), (RockKey(2), true, null)],
            result.Objects.Select(judged => (judged.Change.Key, judged.IsRestored, judged.Refused)));
        Assert.Equal([new TargetId(1)], result.FollowUp.Targets.Where(followed => followed.RestoredTo is null).Select(followed => followed.Target));
    }

    /// <summary>
    /// <see cref="ClashInTheSecondRound"/> with a rock that is one of the cleaned mod's targets going back onto rock 0's old place in
    /// the first round, east of the boulder's landing. In the next round rock 0 would stay, so the target rock would arrive without
    /// support, may not be pulled down as a target, and would stay too. Its refusal names rock 0, whose own refusal names the boulder,
    /// whose follow-up names rock 1: only rock 1 is refused, and the target rock, rock 0 and rock 2 go back.
    /// </summary>
    [Fact]
    public void ARecordArrivingWithoutSupportInANewRoundBlamesOnlyWhatTookItsSupport()
    {
        var onRock0 = TargetRock(2, new Vector3(6 * RockHalfSize + 32, 0, -2 * RockHalfSize), new Vector3(-2 * SearchRadius, 0, 0));
        var clash = ClashInTheSecondRound();
        var scene = clash with { Targets = [.. clash.Targets, onRock0.Target], Unprotected = [1, 2], Rocks = [.. clash.Rocks, onRock0.Change] };

        var result = Plan(scene);

        Assert.Equal(
            [
                (RockKey(0), true, (PlacedRefusal?)null),
                (RockKey(1), false, new PlacedRefusal(PlacedGuard.EarlierRoundWins, RockKey(0), Failure: null)),
                (RockKey(2), true, null),
                (onRock0.Target.Key, true, null),
            ],
            result.Objects.Select(judged => (judged.Change.Key, judged.IsRestored, judged.Refused)).OrderBy(judged => judged.Key, RecordKeyOrder.Comparer));
    }

    /// <summary>
    /// Rock 0 goes back in the first round onto a block, and its follow-up removes the pebble that alone kept the areas of rocks 1 and
    /// 2. A base game boulder the cleaned mod changed, so a cause, rests on rock 1 where the cleaned mod put it, overhanging a slab far
    /// below; the removal steps read the block and the slab as support only. In the next round rock 1 going back would pull the boulder
    /// down onto the slab, too close to rock 0's old place with a too-close distance of 60 units.
    /// </summary>
    private static Scene ClashInTheSecondRound()
    {
        var boulderOnRock1 = new Vector3(3 * RockHalfSize / 2 + RockHalfSize, 0, 3 * RockHalfSize);
        var rock2Now = new Vector3(1000, -3 * SearchRadius, 0);
        return new Scene
        {
            Targets = [RemovedHouse(), PebbleOnRockAt(1, PlaceNow)],
            Unprotected = [1],
            Rocks =
            [
                Rock(0, new Vector3(6 * RockHalfSize, 0, -4 * RockHalfSize), PlaceNow),
                Rock(1, BesidePebbleOnly(PlaceNow), OldPlace),
                Rock(2, new Vector3(1000, -700, 0), rock2Now),
            ],
            Others = [RockNow(0, PlaceNow), RockNow(1, OldPlace), RockNow(2, rock2Now), RockNow(5, boulderOnRock1, Boulder.Base)],
            SupportOnly =
            [
                TestShapes.Placed(BaseGameMod, 6, Boulder.Base, new Vector3(boulderOnRock1.X, 0, -7 * RockHalfSize)),
                TestShapes.Placed(BaseGameMod, 7, Block.Base, new Vector3(6 * RockHalfSize, 0, -6 * RockHalfSize)),
            ],
            TooCloseDistance = 3 * RockHalfSize,
        };
    }

    /// <summary>A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/> that is one of its targets.</summary>
    private static (TargetObject Target, RecordChangedByCleanedMod Change) TargetRock(int targetIndex, Vector3 oldPlace, Vector3 now)
    {
        var target = Own(targetIndex, now) with { Origin = TargetOrigin.Moved };
        return (target, Rock(0, oldPlace, now) with { Key = target.Key, Target = target.Id });
    }

    /// <summary>Where the cleaned mod put the second rock: beyond the areas around every other old place and pebble.</summary>
    private static readonly Vector3 SecondRockNow = new(PlaceNow.X + 2 * SearchRadius, -3 * SearchRadius, 0);

    /// <summary>A pebble of the cleaned mod lying on a rock whose centre is at <paramref name="rockCentre"/>.</summary>
    private static TargetObject PebbleOnRockAt(int index, Vector3 rockCentre) => PebbleAt(index, rockCentre + new Vector3(0, 0, RockHalfSize));

    /// <summary>
    /// An old place whose area holds the pebble on the rock at <paramref name="rockCentre"/> and no other object of the cleaned mod: the
    /// removed house and the other pebbles lie beyond the search radius.
    /// </summary>
    private static Vector3 BesidePebbleOnly(Vector3 rockCentre) => rockCentre + new Vector3(0.8f * SearchRadius, 0, 0);

    /// <summary>
    /// A pebble of the cleaned mod's run that stays touches two rocks the cleaned mod moved: both go back, and the pebble's follow-up
    /// names the first rock it touches, as the also-remove reason of a removal step names the first object it touched.
    /// </summary>
    [Fact]
    public void TwoRocksAnObjectOfTheCleanedModTouchesBothStartItsFollowUp()
    {
        var secondNow = PlaceNow + new Vector3(2 * RockHalfSize, 0, 0);
        var pebble = TestTargets.Create(1, TestTargets.At(PlaceNow + new Vector3(RockHalfSize, 0, RockHalfSize)), Pebble.Base, TestTargets.Space);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = [1],
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, new Vector3(-300, 0, 0), secondNow)],
            Others = [RockNow(0, PlaceNow), RockNow(1, secondNow)],
            FollowUp = TestPlacedGuardRules.DefaultFollowUp with { Mode = FollowUpRemovalMode.EverythingTouchingIt },
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
        var followed = Assert.Single(result.FollowUp.Targets);
        Assert.Equal((pebble.Id, result.Objects[0].Change.Key), (followed.Target, Assert.Single(followed.StartedBy)));
    }
}
