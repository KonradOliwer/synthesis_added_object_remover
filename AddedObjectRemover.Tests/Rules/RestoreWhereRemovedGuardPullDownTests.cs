using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedGuardScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The other mods' objects and the cleaned mod's pieces that the follow-up of rocks going back pulls down or takes along, and the
/// guards they must pass where they land, over in-memory scenes.
/// </summary>
public sealed class RestoreWhereRemovedGuardPullDownTests
{
    /// <summary>
    /// Rock A lies on rock B where the cleaned mod put them. A later mod's crate refuses A, which therefore stays on B. A is no object
    /// of the cleaned mod, so B goes back from under it (decisions 31 and 32).
    /// </summary>
    [Fact]
    public void ARockCarryingARockRefusedInAnEarlierPassGoesBack()
    {
        var aNow = PlaceNow + new Vector3(0, 0, 2 * RockHalfSize);
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, aNow), Rock(1, new Vector3(-300, 0, 0), PlaceNow)],
            Others = [RockNow(0, aNow), RockNow(1, PlaceNow), crate],
        };

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), null],
            Plan(scene).Objects.Select(judged => judged.Refused));
    }

    /// <summary>
    /// A base game lantern stands on top of the rock where the cleaned mod put it. The rock goes back, and the follow-up removes the
    /// lantern, started by the rock, as nothing lies below it in this scene without ground. When a later mod's crate near the rock's
    /// old place refuses the rock, the rock stays and the follow-up of the last pass, which the patch writes, takes nothing away.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATrustedLanternOnARockGoingBackIsTakenAwayOnlyWhenTheRockGoesBack(bool crateRefusesTheRock)
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow), .. crateRefusesTheRock ? [crate] : Array.Empty<OtherObject>()] };

        var result = Plan(scene);

        Assert.Equal(!crateRefusesTheRock, Assert.Single(result.Objects).IsRestored);
        Assert.Equal(
            crateRefusesTheRock ? [] : [(lantern.Key, OtherModsObjectFate.RemovedNothingBelow, RockKey(0).ToString())],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
        Assert.Empty(result.FollowUp.Targets);
    }

    /// <summary>
    /// A base game lantern stands on top of the rock where the cleaned mod put it, far from the rock's old place, and a block of the
    /// cleaned mod that stays lies 60 units below the rock, beyond the zones of every setting. The rock goes back and the follow-up pulls the lantern down onto the
    /// block. When the block's collision group judges it, the lantern where it lands is too close to it, so the rock is refused for the
    /// block and stays, and the lantern with it; otherwise the rock goes back and the lantern is pulled down. The same when the removal
    /// steps read the lantern as support only: pulled down, it stands where no trusted version puts it, so it is a cause where it lands.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ARockWhoseFollowUpPullsAnObjectTooCloseToWhatStaysStays(bool blockJudged, bool lanternSupportOnly)
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, FarPlaceNow + new Vector3(0, 0, RockHalfSize));
        var block = Own(1, FarPlaceNow + new Vector3(0, 0, -100));
        var scene = new Scene
        {
            Targets = [RemovedHouse(), block],
            Rocks = [Rock(0, OldPlace, FarPlaceNow)],
            Others = [.. lanternSupportOnly ? Array.Empty<OtherObject>() : [lantern], RockNow(0, FarPlaceNow)],
            SupportOnly = lanternSupportOnly ? [lantern] : [],
            StandingTooClose = blockJudged ? RestoreSettings.TooClose : null,
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(blockJudged ? (false, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, block.Key, Failure: null)) : (true, null), (rock.IsRestored, rock.Refused));
        Assert.Equal(
            blockJudged ? [] : [(lantern.Key, OtherModsObjectFate.PulledDown, RockKey(0).ToString())],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
    }

    /// <summary>
    /// A base game lantern no plugin changed stands on the first rock where the cleaned mod put it, above a base game slab. The first
    /// rock goes back and the follow-up pulls the lantern down onto the slab, 5 units beside the second rock's old place. The lantern
    /// stands as it stood before the cleaned mod, so the removal steps never take it as a cause, and the second rock still goes back.
    /// </summary>
    [Fact]
    public void AnObjectOnlySupportForTheRemovalStepsPulledBesideACandidateDoesNotKeepIt()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var slab = TestShapes.Placed(BaseGameMod, 6, Block.Base, PlaceNow + new Vector3(0, 0, -100));
        var secondOldPlace = PlaceNow + new Vector3(3 + 5 + RockHalfSize, 0, -80 + RockHalfSize);
        var secondNow = new Vector3(-300, 500, 0);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, secondOldPlace, secondNow)],
            Others = [lantern, RockNow(0, PlaceNow), RockNow(1, secondNow), slab],
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
        var pulled = Assert.Single(result.FollowUp.Others).Outcome;
        Assert.Equal((lantern.Key, OtherModsObjectFate.PulledDown), (pulled.Object.Key, pulled.Fate));
        Assert.Equal(-80f, pulled.NewPosition.Z, tolerance: 0.5f);
    }

    /// <summary>What stands where a candidate pulled down where it goes back would land.</summary>
    public enum AtTheLanding { Nothing, ChangedNavmesh, LaterModsLantern, CleanedModsSlabThatStays }

    /// <summary>
    /// A lantern B stood on rock A before the cleaned mod moved both away. A's old place lies in a navmesh the cleaned mod added, so A
    /// stays, and B would float where it goes back: the follow-up pulls it down onto a slab far below. B stays where the cleaned mod put
    /// it when the place it lands on fails a guard, and the reason names what is there: the navmesh, when it reaches down to where B
    /// lands; a later mod's lantern 5 units beside B there, beyond the reach of every old place; the slab, when it is an object of the
    /// cleaned mod that stays: the check of the slab by its own collision group, as the next patcher makes it, finds B on it and refuses
    /// B itself (109 more of the cleaned mod's objects around are removed, so the areas are cleared). With nothing there, B goes back and
    /// the pull-down is written.
    /// </summary>
    [Theory]
    [InlineData(AtTheLanding.Nothing)]
    [InlineData(AtTheLanding.ChangedNavmesh)]
    [InlineData(AtTheLanding.LaterModsLantern)]
    [InlineData(AtTheLanding.CleanedModsSlabThatStays)]
    public void ACandidatePulledDownWhereItGoesBackMustFitWhereItLands(AtTheLanding there)
    {
        var bNow = new Vector3(-300, 0, 0);
        var slabPlace = OldPlace + new Vector3(0, 0, -300);
        var landing = slabPlace + new Vector3(0, 0, RockHalfSize);
        var lantern = LaterObject(4, Lantern.Base, landing + new Vector3(3 + 5 + 3, 0, 0));
        var ownSlab = there == AtTheLanding.CleanedModsSlabThatStays;
        const int RemovedAround = 110;
        var slab = Own(RemovedAround, slabPlace);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), .. Enumerable.Range(1, RemovedAround - 1).Select(index => Own(index, RemovedHouse().Transform.Position)), .. ownSlab ? [slab] : Array.Empty<TargetObject>()],
            Removed = [.. Enumerable.Range(0, RemovedAround)],
            StandingTooClose = RestoreSettings.TooClose,
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, OldPlace + new Vector3(0, 0, RockHalfSize), bNow, Lantern.Base)],
            Others =
            [
                RockNow(0, PlaceNow), RockNow(1, bNow, Lantern.Base),
                .. ownSlab ? Array.Empty<OtherObject>() : [TestShapes.Placed(BaseGameMod, 3, Block.Base, slabPlace)],
                .. there == AtTheLanding.LaterModsLantern ? [lantern] : Array.Empty<OtherObject>(),
            ],
            Navmesh =
            [
                new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(OldPlace)),
                .. there == AtTheLanding.ChangedNavmesh ? [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(landing + new Vector3(0, 0, 3)))] : Array.Empty<NavmeshWorldTriangle>(),
            ],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.StandsInChangedNavmesh, result.Objects[0].Refused?.Guard);
        PlacedRefusal? refused = there switch
        {
            AtTheLanding.ChangedNavmesh => new PlacedRefusal(PlacedGuard.WouldNotFitWherePulledDown, ChangedNavmeshKey, Failure: null),
            AtTheLanding.LaterModsLantern => new PlacedRefusal(PlacedGuard.WouldNotFitWherePulledDown, lantern.Key, Failure: null),
            AtTheLanding.CleanedModsSlabThatStays => new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, slab.Key, Failure: null),
            _ => null,
        };
        Assert.Equal((refused is null, refused), (result.Objects[1].IsRestored, result.Objects[1].Refused));
        Assert.Equal(
            refused is null ? [(RockKey(1), OtherModsObjectFate.PulledDown, RockKey(1).ToString())] : [],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
    }

    /// <summary>
    /// A pebble the cleaned mod moved onto the rock goes with the rock, and its removal restores it to where it stood before. When a
    /// later mod's lantern stands 5 units beside that place, far beyond the rock's own reach, the pebble would not fit there, so the
    /// rock stays and takes nothing along; otherwise the rock goes back and the pebble is restored.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockWhoseFollowUpRestoresAnObjectThatWouldNotFitStays(bool lanternThere)
    {
        var pebbleBefore = new Vector3(-300, -300, 0);
        var pebble = PebbleAt(1, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = [1],
            RestoredTo = new Dictionary<int, Vector3> { [1] = pebbleBefore },
            EarlierBases = new Dictionary<int, BaseKey> { [1] = Pebble.Base },
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow), .. lanternThere ? [LaterObject(2, Lantern.Base, pebbleBefore + new Vector3(1.5f + 5, 0, 0))] : Array.Empty<OtherObject>()],
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(lanternThere ? (false, new PlacedRefusal(PlacedGuard.TakesAlongObjectThatWouldNotFit, pebble.Key, Failure: null)) : (true, null), (rock.IsRestored, rock.Refused));
        Assert.Equal(lanternThere ? [] : [pebble.Id], result.FollowUp.Targets.Select(followed => followed.Target));
    }

    /// <summary>
    /// Twenty rocks go back, each with a base game lantern on it. The follow-up refuses nothing, so the loop ends after its first pass,
    /// and the follow-up of that pass takes every lantern along, each started by its own rock.
    /// </summary>
    [Fact]
    public void TwentyRocksCarryingLanternsGoBackInOnePass()
    {
        const int Count = 20;
        List<Vector3> places = [.. Enumerable.Range(0, Count).Select(index => PlaceNow + new Vector3(0, 200 * index, 0))];
        var scene = new Scene
        {
            Rocks = [.. places.Select((now, index) => Rock(index, OldPlace + new Vector3(45 * (index % 5), -45 * (index / 5), 0), now))],
            Others =
            [
                .. places.Select((now, index) => RockNow(index, now)),
                .. places.Select((now, index) => TestShapes.Placed(BaseGameMod, 100 + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize))),
            ],
        };

        var result = Plan(scene);

        Assert.Equal(1, result.Passes);
        Assert.All(result.Objects, judged => Assert.True(judged.IsRestored));
        Assert.Equal(
            Enumerable.Range(0, Count).Select(index => RockKey(index).ToString()),
            result.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)));
    }
}
