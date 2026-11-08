using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static RestoreLoopScenes;

/// <summary>
/// The ground that goes back or stays under the other mods' objects in the guards between the kinds that go back where the cleaned
/// mod was removed, repeated until nothing more changes, over in-memory scenes: houses, shrines, lanterns pulled down, ground going
/// back up, and what a new round would undo.
/// </summary>
public sealed class RestoreLoopGroundUnderObjectsTests
{
    /// <summary>
    /// A later mod's house stands on the raised ground, and a base game tree stood on the flat ground the raise buried its foot in: the
    /// ground goes back under both. The later mod is no trusted mod, so its house is left as it is (decision 32), and ground going up
    /// into the tree changes nothing.
    /// </summary>
    [Fact]
    public void GroundGoesBackUnderALaterModsHouseAndABuriedTree()
    {
        var house = LaterObject(10, House.Base, HousePlace);
        var tree = TestShapes.Placed(BaseGameMod, 11, Tree.Base, new Vector3(3000, 1000, 0));
        var scene = new Scene { Others = [house, tree], Ground = RaisedGround() };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal((0f, 0f), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(23, 8)]));
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject);
        Assert.Empty(result.FollowUp.Others);
    }

    /// <summary>
    /// A base game shrine stands on the raised ground: once the ground goes back it would float. It is pulled down onto the ground going
    /// back, or removed, started by the ground alone; with KeepSupportingObject the ground under it stays raised.
    /// </summary>
    [Theory]
    [InlineData(OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(OtherModsObjectsHandling.RemoveFloatingObject)]
    [InlineData(OtherModsObjectsHandling.KeepSupportingObject)]
    public void ATrustedModsShrineOnTheRaisedGroundGoesWithTheGroundOrKeepsIt(OtherModsObjectsHandling handling)
    {
        var shrine = TestShapes.Placed(BaseGameMod, 10, Block.Base, HousePlace with { Z = Raised + RockHalfSize });
        var scene = new Scene { Others = [shrine], Ground = RaisedGround(), Support = Support with { Handling = handling }, Trust = BaseGameTrusted };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        if (handling == OtherModsObjectsHandling.KeepSupportingObject)
        {
            Assert.Equal(Raised, land.FinalHeights![PointAt(8, 16)]);
            Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject && kept.Points.Contains(PointAt(8, 16)));
            Assert.Empty(result.FollowUp.Others);
            return;
        }
        Assert.Equal(0f, land.FinalHeights![PointAt(8, 16)]);
        var followed = Assert.Single(result.FollowUp.Others);
        Assert.Equal((shrine.Key, true, 0), (followed.Outcome.Object.Key, followed.ByGround, followed.StartedBy.Length));
        if (handling == OtherModsObjectsHandling.RemoveFloatingObject) Assert.True(followed.Outcome.IsRemoved);
        else Assert.Equal(RockHalfSize, followed.Outcome.NewPosition.Z, 0.5f);
    }

    /// <summary>
    /// The cleaned mod raised cell (0, 0) by 504 units, a whole number of height steps. A base game rock it moved onto the raised south-west quarter goes back in the
    /// first round to the flat ground just east of that quarter, and its follow-up removes the cleaned mod's block on it, which alone
    /// kept that quarter. In the next round the quarter's ground would go back down, and a base game shrine the cleaned mod changed on
    /// its east edge would be pulled down beside the rock's old place, too close to it (with a too-close distance of 200 units), so the
    /// rock would stay and the block with it. The earlier round wins: nothing of the quarter goes back, its heights and its vertex colours
    /// alike, reported with its current share (cleared) and what it is held for, the other quarters' colours go back, the rock goes back
    /// and the block is removed. Three rounds: the first, the one undoing it, and its second try.
    /// </summary>
    [Fact]
    public void ALandQuarterANewRoundWouldClearStaysAsTheEarlierRoundLeftItWhenItsGroundGoingDownWouldUndoThatRound()
    {
        var allPoints = PointSet.Of(Enumerable.Range(0, TerrainGrid.VertexCount));
        const float high = 504f;
        var rockNow = new Vector3(QuarterCentre, 300, high + RockHalfSize);
        var rockOldPlace = new Vector3(CellCentre + 162, QuarterCentre, RockHalfSize);
        var shrine = RockNow(10, new Vector3(CellCentre - RockHalfSize, QuarterCentre, high + RockHalfSize));
        var scene = new Scene
        {
            Rocks = [Rock(0, rockOldPlace, rockNow)],
            OwnStanding = [rockNow + new Vector3(0, 0, 2 * RockHalfSize)],
            Others = [RockNow(0, rockNow), shrine],
            Lands = [RaisedLand() with { Heights = new LandHeights(HeightsAll(0f), HeightsAll(high)), UndoColours = allPoints }],
            Ground = GroundAll(high),
            Trust = BaseGameTrusted,
            Settings = AllOn with { TooClose = AllOn.TooClose with { DistanceInUnits = 200f } },
        };

        var result = Plan(scene);

        Assert.Equal(3, result.Rounds);
        Assert.True(Assert.Single(result.Objects).IsRestored);
        var land = Assert.Single(result.Lands);
        var kept = Assert.Single(result.KeptForEarlierRound);
        var rock = Rock(0, rockOldPlace, rockNow).Key;
        Assert.Equal((land.Change.LandKey, new EarlierRoundHold(rock, CauseNamed: true)), (kept.Change, kept.Hold));
        Assert.All(land.Quarters, quarter => Assert.Equal(AreaVerdict.Cleared, quarter.Verdict));
        var held = Assert.Single(land.ChangedQuarters, quarter => quarter.Quarter == kept.Quarter);
        Assert.Equal((false, rock, AreaVerdict.Cleared), (held.Restored.Any, held.HeldForEarlierRound?.Undone, held.Area!.Verdict));
        Assert.All(land.ChangedQuarters.Where(quarter => quarter != held), quarter => Assert.True(quarter.Restored.Colours > 0));
        Assert.Equal([new TargetId(RemovedTargets().Count)], result.FollowUp.Targets.Where(followed => followed.RestoredTo is null).Select(followed => followed.Target));
        Assert.DoesNotContain(result.FollowUp.Others, other => other.Outcome.Object.Key == shrine.Key);
    }

    /// <summary>
    /// In cell (1, 0), with no land: navmesh M goes back in the first round, and the follow-up of a rock going back removes the cleaned
    /// mod's block that alone kept the areas of navmesh N, which names M as a neighbour, and of rock 2. In the next round N would join
    /// M's group, and a later plugin changed N's presence again, so the group would be refused and M undone. The refusal names N: N is
    /// held, planned as in the round before so it joins no group, and shows its current share and what it is held for; M goes back
    /// alone, and rock 2 goes back too.
    /// </summary>
    [Fact]
    public void ANavmeshANewRoundWouldGroupWithOneThatWentBackIsHeldAndTheOthersGoBack()
    {
        var m = MovedNavmesh() with { Earlier = Square(BaseGame, new Vector3(5120, 1500, 0)), Current = Square(TestTargets.TargetName, new Vector3(5120, 1600, 0)) };
        var n = MovedNavmesh() with
        {
            Navmesh = HeldNavmeshKey,
            Presence = new PartOutcome.LeftToLaterPlugin(LaterMod.ToPluginName()),
            Earlier = Square(BaseGame, new Vector3(7168, 2500, 0)),
            Current = Square(TestTargets.TargetName, ClearedByTheFollowUp),
            Neighbours = [new NeighbourNavmesh(NavmeshKey, m.Current, m.Earlier)],
        };
        var scene = SecondRoundScene() with { Navmeshes = [m, n], Lands = [] };

        var result = Plan(scene);

        Assert.Equal(3, result.Rounds);
        Assert.All(result.Objects, judged => Assert.True(judged.IsRestored));
        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal(NavmeshKey, Assert.Single(group.Members));
        Assert.NotNull(group.Write);
        var held = Assert.Single(result.Navmeshes, navmesh => navmesh.Change.Navmesh == HeldNavmeshKey);
        Assert.Equal((NavmeshKey, AreaVerdict.Cleared, (AreaVerdict?)null, (NavmeshUndo?)null), (held.HeldForEarlierRound?.Undone, held.Area!.Verdict, held.KeptForArea, held.Undo));
        var row = RestoreOutcomes.KeptBecause(new NavmeshOutcome(held, Group: null), key => key.ToString());
        Assert.Equal($"undoing what an earlier round of the restore put back or removed ({NavmeshKey})", row);
    }

    /// <summary>
    /// <see cref="SecondRoundScene"/> with the raised land of cell (0, 0), of which only one point of the south-west quarter goes back,
    /// and navmesh N, whose earlier shape lies on that point and whose area the block alone kept. The quarter goes back in the first
    /// round. In the next round N would go back onto the raised ground and keep the point there, so the quarter would stay. The quarter
    /// is blamed by its own cause, N lying in it: N is held, the quarter goes back again, and the rocks go back.
    /// </summary>
    [Fact]
    public void AQuarterANewRoundWouldKeepBlamesWhatNewlyLiesInItAndTheOthersGoBack()
    {
        var onePoint = PointAt(8, 8);
        var n = MovedNavmesh() with
        {
            Navmesh = HeldNavmeshKey,
            Earlier = Square(BaseGame, new Vector3(QuarterCentre, QuarterCentre, Raised)),
            Current = Square(TestTargets.TargetName, ClearedByTheFollowUp),
        };
        var scene = SecondRoundScene() with { Navmeshes = [n], Lands = [RaisedLand() with { UndoPoints = PointSet.Of([onePoint]) }], Ground = RaisedGround() };

        var result = Plan(scene);

        Assert.Equal(3, result.Rounds);
        Assert.All(result.Objects, judged => Assert.True(judged.IsRestored));
        var land = Assert.Single(result.Lands);
        Assert.True(Assert.Single(land.ChangedQuarters).Restored.Any);
        Assert.Equal(0f, land.FinalHeights![onePoint]);
        Assert.Equal(new EarlierRoundHold(land.Change.LandKey, CauseNamed: true), Assert.Single(result.Navmeshes).HeldForEarlierRound);
    }

    private static readonly RecordKey HeldNavmeshKey = new(BaseGame, 0x704);

    /// <summary>Where the area holds the cleaned mod's block on rock 0 and removed objects only.</summary>
    private static readonly Vector3 ClearedByTheFollowUp = new(7168, 2550, 0);

    /// <summary>
    /// In cell (1, 0): rock 0 goes back in the first round, and its follow-up removes the cleaned mod's block on it, which alone keeps
    /// the area of rock 2's old place and of <see cref="ClearedByTheFollowUp"/>; the base game is trusted in full.
    /// </summary>
    private static Scene SecondRoundScene()
    {
        var rock0Now = new Vector3(7168, 2000, RockHalfSize);
        var rock2Now = new Vector3(12000, 0, RockHalfSize);
        return new Scene
        {
            Rocks = [Rock(0, new Vector3(5120, 3500, RockHalfSize), rock0Now), Rock(2, new Vector3(7868, 2000, RockHalfSize), rock2Now)],
            OwnStanding = [rock0Now + new Vector3(0, 0, 2 * RockHalfSize)],
            Others = [RockNow(0, rock0Now), RockNow(2, rock2Now)],
            Trust = BaseGameTrusted,
        };
    }

    /// <summary>
    /// The cleaned mod lowered cell (0, 0) from 80 units to flat ground, and a base game shrine shown as before the cleaned mod stands on
    /// the lowered ground. It stood on the ground going back, so the ground goes back up into it whole and nothing is followed up.
    /// </summary>
    [Fact]
    public void GroundGoingBackUpIntoATrustedModsShrineShownAsBeforeTheCleanedModGoesBack()
    {
        var shrine = TestShapes.Placed(BaseGameMod, 10, Block.Base, HousePlace with { Z = RockHalfSize });
        var scene = new Scene { Others = [shrine], Lands = [LoweredLand()], Ground = GroundAll(0f), Trust = BaseGameTrusted };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal(Raised, land.FinalHeights![PointAt(8, 16)]);
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject);
        Assert.Empty(result.FollowUp.Others);
    }

    /// <summary>
    /// As above, but the shrine was placed for the lowered ground: a later mod placed it there, or the cleaned mod moved a base game
    /// shrine there and its area is not cleared. The ground going back up would bury it: when its plugins are trusted in full, the
    /// ground under it stays lowered and the rest of the cell goes back; an untrusted later mod's shrine keeps nothing.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GroundGoingBackUpStaysUnderATrustedObjectPlacedForTheLoweredGround(bool laterModsShrine, bool trusted)
    {
        var place = HousePlace with { Z = RockHalfSize };
        var shrine = laterModsShrine
            ? LaterObject(10, Block.Base, place) with { ChangeSinceBeforeCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null) }
            : TestShapes.Placed(BaseGameMod, 10, Block.Base, place) with
            {
                WinningMod = TestTargets.TargetName,
                WinningModPastPatches = TestTargets.TargetName,
                ChangeSinceBeforeCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame),
            };
        var scene = new Scene { Others = [shrine], Lands = [LoweredLand()], Ground = GroundAll(0f), Trust = trusted ? LaterModTrusted : BaseGameTrusted };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal((trusted ? 0f : Raised, Raised), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(23, 8)]));
        Assert.Equal(trusted, land.Kept.Any(kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject && kept.Points.Contains(PointAt(8, 16))));
        Assert.Empty(result.FollowUp.Others);
    }

    /// <summary>
    /// As above, but the shrine is the cleaned mod's own, which a trusted later mod holds: it is one of the cleaned mod's objects that
    /// stay, so the check of what the ground going back up would bury covers it (asked of the whole cell going back up, since the
    /// ground under the shrine itself stays), and it is never followed up.
    /// </summary>
    [Fact]
    public void TheGroundGoingBackUpWouldBuryAShrineOfTheCleanedModATrustedLaterModHolds()
    {
        var place = HousePlace with { Z = RockHalfSize };
        var shrine = TestShapes.Placed(ModKey.FromNameAndExtension(TestTargets.TargetName.FileName), 10, Block.Base, place) with
        {
            WinningMod = LaterMod.ToPluginName(),
            WinningModPastPatches = LaterMod.ToPluginName(),
            ChangeWithoutCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null),
            ChangeSinceBeforeCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null),
        };
        var overridden = TestTargets.Create(0, TestTargets.At(place), Block.Base, TestTargets.Space) with { Key = shrine.Key };
        var scene = new Scene { Others = [shrine], OverriddenLater = [overridden], Lands = [LoweredLand()], Ground = GroundAll(0f), Trust = LaterModTrusted };

        var planned = WhereRemovedRestorer.BuildPlans(InputOf(scene));
        var loop = RestoreGuardsBuilder.Build(planned.Inputs, planned.Plans).LoopInputs;
        var buried = loop.BuriedByGroundGoingUp.On(GroundAll(Raised));

        Assert.Equal([shrine.Key], buried.Select(other => other.Key));
        Assert.Empty(Plan(scene).FollowUp.Others);
    }

    /// <summary>
    /// As above, but the trusted later mod was made for the ground before the cleaned mod: it sank its shrine 30 units into that ground,
    /// so the shrine now floats over the lowered ground. Once the ground goes back up the shrine sits as its mod placed it, so the
    /// ground goes back under it too.
    /// </summary>
    [Fact]
    public void GroundGoingBackUpUnderATrustedObjectPlacedForTheEarlierGroundGoesBack()
    {
        const float Sunk = 30f;
        var shrine = LaterObject(10, Block.Base, HousePlace with { Z = Raised - Sunk + RockHalfSize }) with
        {
            ChangeSinceBeforeCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null),
        };
        var scene = new Scene { Others = [shrine], Lands = [LoweredLand()], Ground = GroundAll(0f), Trust = LaterModTrusted };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal((Raised, Raised), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(23, 8)]));
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject);
    }

    /// <summary>
    /// A base game lantern no plugin changed stood on an object the removal steps removed, and they pulled it down onto the ground the
    /// cleaned mod lowered. The patch placed it for that ground, so the ground going back up stays under it. A lantern that stands
    /// there as before the cleaned mod stood on the ground going back, so that ground goes back under it. An untrusted later mod's
    /// lantern the removal steps pulled down keeps nothing.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GroundGoingBackUpStaysUnderAnObjectTheRemovalStepsPulledDownOntoTheLoweredGround(bool pulledDown, bool untrustedLantern)
    {
        const float Drop = 200f;
        var standsNow = HousePlace with { Z = RockHalfSize };
        var stoodAt = pulledDown ? standsNow with { Z = standsNow.Z + Drop } : standsNow;
        // The only object, so the collector numbers it 0.
        var lantern = (untrustedLantern ? LaterObject(10, Block.Base, stoodAt) : TestShapes.Placed(BaseGameMod, 10, Block.Base, stoodAt)) with { Id = new OtherId(0) };
        var pulled = new OtherModsObjectOutcome(lantern, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop, ReferenceRoles.None);
        var scene = new Scene
        {
            Others = [lantern],
            Lands = [LoweredLand()],
            Ground = GroundAll(0f),
            Trust = BaseGameTrusted,
            OtherMods = pulledDown ? new OtherModsObjectsResult(StepOutcome.Performed, [pulled], [], [], Failure: null) : OtherModsObjectsResult.SwitchedOff,
        };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        var kept = pulledDown && !untrustedLantern;
        Assert.Equal((kept ? 0f : Raised, Raised), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(23, 8)]));
        Assert.Equal(kept, land.Kept.Any(each => each.Reason == TerrainKeptReason.UnderOtherModsObject && each.Points.Contains(PointAt(8, 16))));
    }

    /// <summary>
    /// The cleaned mod dug cell (0, 0) down from 80 units to flat ground in its five western columns of grid points; the ground east of
    /// them was flat before it. A trusted later mod's log lies across the edge of that hollow, so the ground under its eastern half
    /// stays as it is. Placed for the lowered ground, the ground under it stays lowered. Placed for the ground before the cleaned mod,
    /// sunk 30 units into it, the ground goes back under it: only the places where the ground goes up tell what it was placed for.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GroundGoingBackUpUnderATrustedObjectAcrossTheEdgeOfTheLoweredGroundFollowsWhatItWasPlacedFor(bool placedForTheLoweredGround)
    {
        const int HollowColumns = 6;
        const float Sunk = 30f;
        float[] before = [.. Enumerable.Range(0, TerrainGrid.VertexCount).Select(point => point % Side < HollowColumns ? Raised : 0f)];
        var log = LaterObject(10, Log.Base, new Vector3(600, 2000, placedForTheLoweredGround ? 0f : Raised - Sunk)) with
        {
            ChangeSinceBeforeCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null),
        };
        var scene = new Scene { Others = [log], Lands = [RaisedLand() with { Heights = new LandHeights(before, HeightsAll(0f)) }], Ground = GroundAll(0f), Trust = LaterModTrusted };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal((placedForTheLoweredGround ? 0f : Raised, Raised), (land.FinalHeights![PointAt(3, 16)], land.FinalHeights[PointAt(2, 4)]));
        Assert.Equal(placedForTheLoweredGround, land.Kept.Any(kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject && kept.Points.Contains(PointAt(3, 16))));
    }

    /// <summary>The LAND record of cell (0, 0), which the cleaned mod lowered from <see cref="Raised"/> to flat ground.</summary>
    private static LandChangedByCleanedMod LoweredLand() => RaisedLand() with { Heights = new LandHeights(HeightsAll(Raised), HeightsAll(0f)) };

    /// <summary>The base game and Later.esp trusted in full.</summary>
    private static readonly ModTrustTable LaterModTrusted =
        TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [LaterMod.ToPluginName()], untrusted: [], baseGame: [BaseGame]);

    /// <summary>
    /// The cleaned mod raised cell (0, 0) by 1000 units, and a base game shrine no plugin changed stands on it, which the removal steps
    /// read as support only. A base game block the cleaned mod moved stays buried on the flat ground below, 250 units east of the shrine,
    /// and the too-close step judges it within 300 units. Pulled down onto the ground going back, the shrine would stand where no trusted
    /// version puts it, so the next patcher would read it as a cause, too close to the block: the ground under the shrine stays and its
    /// pull-down is not written; the ground beyond it goes back. With a rock going back elsewhere or with nothing going back (the
    /// check runs without a placed record going back too), the same.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGroundUnderAShrineTheGroundGoingDownWouldPullTooCloseToWhatStaysIsKept(bool rockGoingBack)
    {
        const float High = 1000f;
        var shrine = TestShapes.Placed(BaseGameMod, 10, Block.Base, HousePlace with { Z = High + RockHalfSize });
        var scene = new Scene
        {
            Rocks = rockGoingBack ? [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)] : [],
            Others = rockGoingBack ? [shrine, RockNow(0, RockNowPlace)] : [shrine],
            Lands = [RaisedLand() with { Heights = new LandHeights(HeightsAll(0f), HeightsAll(High)) }],
            Ground = GroundAll(High),
            Trust = BaseGameTrusted,
            Standing = [HousePlace with { X = HousePlace.X + 2 * RockHalfSize + 250f, Z = RockHalfSize }],
            StandingTooClose = AllOn.TooClose with { DistanceInUnits = 300f },
        };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal((High, 0f), (land.FinalHeights![PointAt(8, 16)], land.FinalHeights[PointAt(11, 16)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderOtherModsObject && kept.Points.Contains(PointAt(8, 16)));
        Assert.Empty(result.FollowUp.Others);
        Assert.All(result.Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
        Assert.Equal(rockGoingBack ? 1 : 0, result.Objects.Count());
    }

    /// <summary>Twelve base game shrines stand on the raised ground: the ground going down pulls each down alike on 1 and 8 workers.</summary>
    [Fact]
    public void TheGroundGoingDownGivesTheSameResultsWhateverTheNumberOfWorkers()
    {
        Scene Shrines(int workers) => new()
        {
            Others = [.. Enumerable.Range(0, 12).Select(index => TestShapes.Placed(BaseGameMod, 10 + index, Block.Base, new Vector3(300 + 300 * (index % 4), 1000 + 800 * (index / 4), Raised + RockHalfSize)))],
            Ground = RaisedGround(),
            Trust = BaseGameTrusted,
            Workers = workers,
        };

        var (one, eight) = (Plan(Shrines(1)), Plan(Shrines(8)));

        Assert.Equal(12, one.FollowUp.Others.Count());
        Assert.Equal(
            one.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, other.Outcome.NewPosition, other.ByGround)),
            eight.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, other.Outcome.NewPosition, other.ByGround)));
        Assert.Equal(Assert.Single(one.Lands).FinalHeights, Assert.Single(eight.Lands).FinalHeights);
    }
}
