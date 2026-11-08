using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static RestoreLoopScenes;

/// <summary>
/// The ground and the standing objects in the guards between the kinds that go back where the cleaned mod was removed, repeated until
/// nothing more changes, over in-memory scenes: refused rocks keep the ground under them, the ground around an object that stays goes
/// back, copies, and ground a later plugin changed.
/// </summary>
public sealed class RestoreLoopTests
{
    /// <summary>A later mod's crate stands next to the rock's old place, so the rock is refused; the ground under the rock where it stands now stays.</summary>
    [Fact]
    public void ARefusedRockKeepsTheGroundUnderItWhereItStandsNow()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)] };

        var result = Plan(scene);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), Assert.Single(result.Objects).Refused);
        var land = Assert.Single(result.Lands);
        Assert.Equal(Raised, land.FinalHeights![PointAt(12, 20)]);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged && kept.Points.Contains(PointAt(12, 20)));
        Assert.Equal(0f, land.FinalHeights[PointAt(12, 12)]);
    }

    /// <summary>As above, but the rock stands hidden far below the ground now, so it keeps no ground.</summary>
    [Fact]
    public void ARefusedRockHiddenFarBelowTheGroundKeepsNoGround()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, -30000f);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)] };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(0f, land.FinalHeights![PointAt(12, 20)]);
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged);
    }

    /// <summary>
    /// Rock A is refused next to a later mod's crate, so the ground under it where it stands now stays raised (grid rows 18 to 21) and
    /// the ground up to two grid steps from there is blended. Rock B's old place lies on that blended ground, which the patch leaves
    /// above the flat ground B stood on: B would go back into the cleaned mod's ground, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockGoingBackIntoTheBlendAroundGroundARefusedRockKeepsIsRefused(bool baseGameTrusted)
    {
        var aOldPlace = new Vector3(1500, 1500, RockHalfSize);
        var aNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var bOldPlace = new Vector3(12 * TerrainGrid.VertexSpacing, 16 * TerrainGrid.VertexSpacing, RockHalfSize);
        var crate = LaterObject(10, Block.Base, aOldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, aOldPlace, aNow), Rock(1, bOldPlace, RockNowPlace)],
            Others = [crate, RockNow(0, aNow), RockNow(1, RockNowPlace)],
            Ground = RaisedGround(),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Settings = AllOn with { TerrainEdgeBlendDistance = 2 * TerrainGrid.VertexSpacing },
        };

        var result = Plan(scene);

        Assert.Equal(new PlacedGuard?[] { PlacedGuard.TooCloseToWhatStays, PlacedGuard.ArrivesInGround }, result.Objects.Select(judged => judged.Refused?.Guard));
        var land = Assert.Single(result.Lands);
        Assert.True(land.Heights.Contains(PointAt(12, 17)));
        Assert.NotEqual(0f, land.FinalHeights![PointAt(12, 17)]);
    }

    /// <summary>
    /// A landscape mod after the cleaned mod raised the ground of cell (0, 0), whose LAND record the cleaned mod does not override, by
    /// the given height at the rock's old place. The world without the cleaned mod has that ground too, so the rock goes back onto it.
    /// </summary>
    [Theory]
    [InlineData(150f)]
    [InlineData(4f)]
    public void ARockGoingBackOntoGroundOnlyAnUnrelatedPluginRaisedIsNotRefused(float raisedBy)
    {
        var flat = new float[TerrainGrid.VertexCount];
        var raised = Enumerable.Repeat(raisedBy, TerrainGrid.VertexCount).ToArray();
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            ReadHeights = cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? new LandHeights(flat, raised) : new LandHeights(null, null),
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
    }

    /// <summary>A landscape mod after the cleaned mod lowered the flat ground of cell (0, 0) by 150 units; the world without the cleaned mod has it lowered too.</summary>
    [Fact]
    public void ARockGoingBackOntoGroundOnlyAnUnrelatedPluginLoweredIsNotRefused()
    {
        var rock = Assert.Single(Plan(GroundChangedLater(new Vector3(1500, 1500, RockHalfSize), earlier: 0f, final: -150f)).Objects);

        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
    }

    /// <summary>
    /// The rock goes back high over cell (0, 0), as a piece hanging over a cliff does; a landscape mod after the cleaned mod changed the
    /// ground under it far below it, or far above it, so the rock neither sinks nor floats.
    /// </summary>
    [Theory]
    [InlineData(0f, 150f)]
    [InlineData(3000f, 2000f)]
    public void ARockGoingBackAboveGroundALaterPluginChangedFarBelowItIsNotRefused(float earlier, float final)
    {
        var rock = Assert.Single(Plan(GroundChangedLater(new Vector3(1500, 1500, 1000f), earlier, final)).Objects);

        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
    }

    private const float RaisedLater = 150f;

    /// <summary>The rock where a later plugin moved it, on the ground it raised.</summary>
    private static readonly Vector3 OnGroundRaisedLater = RockNowPlace with { Z = RaisedLater + RockHalfSize };

    /// <summary>
    /// The cleaned mod moved the rock and gave it a sound radius; a later plugin moved it again onto ground it raised, and the rock
    /// keeps that position. Only the radius goes back, which shapes nothing, so the rock stays on the ground as it is.
    /// </summary>
    [Fact]
    public void AnObjectWhosePositionALaterPluginKeepsIsNotRefusedForGroundChangedUnderIt()
    {
        var rock = RockKeepingALaterPosition(PlacedParts.BaseAndData, earlier => earlier with { Reference = TestPlacedRecords.PlainReference with { ReferenceRadius = 512f } });

        var judged = Assert.Single(Plan(GroundChangedLater(OnGroundRaisedLater, earlier: 0f, final: RaisedLater) with { Rocks = [rock], Others = [RockNow(0, OnGroundRaisedLater)] }).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// The cleaned mod moved the rock onto the ground it raised and gave it a sound radius; a later plugin keeps that position. Only
    /// the radius goes back, so the rock stands where it is, and the ground under it stays raised while the rest of the cell goes back.
    /// </summary>
    [Fact]
    public void TheGroundUnderAnObjectGoingBackInItsPresentShapeIsKept()
    {
        var moved = Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace);
        var rock = moved with
        {
            Earlier = moved.Earlier with
            {
                Undo = PlacedParts.BaseAndData,
                Reference = TestPlacedRecords.PlainReference with { ReferenceRadius = 512f },
                LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Position, LaterMod.ToPluginName()),
            },
        };

        var result = Plan(new Scene { Rocks = [rock], Others = [RockNow(0, RockNowPlace)] });

        Assert.Equal((true, null), (Assert.Single(result.Objects).IsRestored, Assert.Single(result.Objects).Refused));
        var land = Assert.Single(result.Lands);
        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(27, 12)], land.FinalHeights[PointAt(12, 12)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectThatStays && kept.Points.Contains(PointAt(27, 12)));
    }

    /// <summary>
    /// An object the cleaned mod moved stands in cell (1, 0), far enough from cell (0, 0) to leave its areas cleared. The
    /// changed-terrain rule checks the ground 1500 units around it, which reaches the raised ground of cell (0, 0). The next patcher
    /// reads the heights going back as given back, so it accepts them whatever the trust of the version below the cleaned mod's, and
    /// they go back.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TheGroundAroundAnObjectThatStaysGoesBackWhateverTheTrustOfItsEarlierVersion(bool judgedByTheStep, bool earlierFromTheBaseGame)
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)],
            StandingTerrainCheck = judgedByTheStep ? TerrainCheckReaching(TerrainCheckDistance) : null,
            EarlierLandPlugin = earlierFromTheBaseGame ? BaseGame : LaterMod.ToPluginName(),
        };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal((0f, 0f), (land.FinalHeights![PointAt(31, 8)], land.FinalHeights[PointAt(12, 8)]));
        Assert.DoesNotContain(land.Kept, each => each.Reason == TerrainKeptReason.ForChangedTerrainCheck);
    }

    /// <summary>
    /// As above, and the cleaned mod also painted every quarter of cell (0, 0). Every quarter goes back, also when the rule judging the
    /// object that stays counts terrain type changes and the version below the cleaned mod's is one the rule does not accept: the next
    /// patcher reads the textures as given back.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TheTexturesAroundAnObjectThatStaysGoBackWhateverTheTrustOfTheirEarlierVersion(bool countsTypes, bool earlierFromTheBaseGame)
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)],
            StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance) with { CountHeightChanges = false, CountTerrainTypeChanges = countsTypes },
            Lands = [RaisedLand() with { UndoQuarters = QuarterSet.Of([0, 1, 2, 3]) }],
            EarlierLandPlugin = earlierFromTheBaseGame ? BaseGame : LaterMod.ToPluginName(),
            LandLayers = PaintedPerPlugin,
        };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal([0, 1, 2, 3], land.Textures.Quarters);
        Assert.False(Assert.Single(land.ChangedQuarters, quarter => quarter.Quarter == 1).TexturesKeptForObjectThatStays);
    }

    /// <summary>
    /// The object that stays in cell (1, 0), with the water level of cell (0, 0) the cleaned mod lowered from 50 to −100 over ground it
    /// left flat, which nothing stands across. The level goes back, also when the rule judging the object counts water changes, its
    /// area reaches cell (0, 0) and the earlier level is an untrusted plugin's: the next patcher reads the level as given back.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TheWaterLevelAroundAnObjectThatStaysGoesBackWhateverTheTrustOfItsEarlierVersion(bool countsWater, bool earlierFromTheBaseGame)
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)],
            StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance) with { CountHeightChanges = false, CountWaterHeightChanges = countsWater },
            Cells = [earlierFromTheBaseGame ? ChangedWater() : ChangedUntrustedWater()],
            Lands = [],
            ViewLands = [FlatLand()],
        };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel)));
    }

    /// <summary>
    /// As above, counting height changes too, over the ground the cleaned mod raised above both levels from an untrusted plugin's: the
    /// level going back covers none of the accepted raised ground, so the rule counts no water change, and the level goes back.
    /// </summary>
    [Fact]
    public void AWaterLevelWhoseLineMovesAcrossNoGroundTheRuleReadsGoesBack()
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)],
            StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance) with { CountWaterHeightChanges = true },
            Cells = [ChangedUntrustedWater()],
            EarlierLandPlugin = LaterMod.ToPluginName(),
        };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel)));
    }

    /// <summary>
    /// The cleaned mod moved a base game rock from the cleared south-west quarter of cell (0, 0) to its south-east quarter, beside an
    /// object of its own that stays, so the rock goes back with a copy left there, and that quarter's ground stays. The changed-terrain
    /// rule checks the ground 1500 units around the copy with the settings of the cleaned mod's own objects, and reads the heights
    /// going back as given back, so the cleared north-east quarter goes back whatever the trust of the version below the cleaned mod's.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheGroundUnderACopyIsKeptAndTheGroundAroundItGoesBack(bool ownCheckOn, bool earlierUntrusted)
    {
        var scene = CopyScene(ownCheckOn ? TerrainCheckReaching(TerrainCheckDistance) : null) with { EarlierLandPlugin = earlierUntrusted ? LaterMod.ToPluginName() : BaseGame };

        var result = Plan(scene);

        var judged = Assert.Single(result.Objects);
        Assert.Equal((true, new CloneJudged(RefusedBy: null)), (judged.IsRestored, judged.Cloning));
        var land = Assert.Single(result.Lands);
        Assert.Equal((Raised, 0f, 0f), (land.FinalHeights![PointAt(27, 12)], land.FinalHeights[PointAt(17, 20)], land.FinalHeights[PointAt(12, 12)]));
        Assert.DoesNotContain(land.Kept, each => each.Reason == TerrainKeptReason.ForChangedTerrainCheck);
    }

    /// <summary>
    /// As above, and the cleaned mod also painted every quarter of cell (0, 0) over an untrusted plugin's textures. When the cleaned
    /// mod's own changed-terrain check counts terrain type changes, the cleared quarters its area around the copy reaches keep their
    /// textures; otherwise they go back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTexturesWhereTheCleanedModsOwnTerrainCheckWouldCountThemAroundACopyAreKept(bool countsTypes)
    {
        var scene = CopyScene(TerrainCheckReaching(TerrainCheckDistance) with { CountHeightChanges = false, CountTerrainTypeChanges = countsTypes }) with
        {
            Lands = [RaisedLand() with { UndoQuarters = QuarterSet.Of([0, 1, 2, 3]) }],
            EarlierLandPlugin = LaterMod.ToPluginName(),
            LandLayers = PaintedPerPlugin,
        };

        var result = Plan(scene);

        Assert.True(Assert.Single(result.Objects).IsRestored);
        var land = Assert.Single(result.Lands);
        Assert.Equal(countsTypes ? [] : [0, 2, 3], land.Textures.Quarters);
        Assert.Equal(countsTypes, Assert.Single(land.ChangedQuarters, quarter => quarter.Quarter == 3).TexturesKeptForObjectThatStays);
    }

    /// <summary>
    /// As above, with the water level of cell (0, 0) the cleaned mod lowered from an untrusted plugin's level over ground it left
    /// flat, which nothing stands across. The level goes back, also when the cleaned mod's own changed-terrain check counts water
    /// changes around the copy: the next patcher reads the level as given back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWaterLevelAroundACopyGoesBackWhenItsEarlierVersionIsUntrusted(bool countsWater)
    {
        var scene = CopyScene(TerrainCheckReaching(TerrainCheckDistance) with { CountHeightChanges = false, CountWaterHeightChanges = countsWater }) with
        {
            Cells = [ChangedUntrustedWater()],
            Lands = [],
            ViewLands = [FlatLand()],
        };

        var result = Plan(scene);

        Assert.True(Assert.Single(result.Objects).IsRestored);
        var cell = Assert.Single(result.Cells);
        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.Undone.Any(part => part.Name.Part == CellPart.WaterLevel)));
    }

    /// <summary>
    /// A rule that finds the heights of the southern rows of cell (0, 0) changed around the object that stays, over every plan. When the
    /// load order as read shows the same finding, it is no change the restore makes, and nothing is kept for it. Otherwise those rows
    /// stay raised, the northern ones go back, and the next pass, which still reads the north, finds it again with nothing more to keep:
    /// it is counted as left. The check over the load order runs once, whatever the passes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFindingTheLoadOrderAsReadAlreadyHasKeepsNothing(bool baselineFinds)
    {
        var finding = new TerrainCheckOutcome(HeightsOfTheSouthOfTheRaisedCellChanged(), Failure: null);
        var rule = new TestTerrainJudging(baselineFinds ? finding : default, finding, new HashSet<ExteriorCell> { RaisedCell });
        var scene = new Scene { Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)], StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance), TerrainRule = rule };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal(baselineFinds ? 0f : Raised, land.FinalHeights![PointAt(12, 12)]);
        Assert.Equal(0f, land.FinalHeights[PointAt(12, SouthernRows + 4)]);
        Assert.Equal(!baselineFinds, land.Kept.Any(kept => kept.Reason == TerrainKeptReason.ForChangedTerrainCheck && kept.Points.Contains(PointAt(12, 12))));
        Assert.Equal((baselineFinds ? 0 : 1, baselineFinds ? 1 : 2), (result.TerrainFindingsLeft, result.Passes));
        Assert.Single(rule.Checked);
        Assert.Equal(result.Passes, rule.CheckedOverPlans.Count);
    }

    /// <summary>
    /// A rule that finds the water of cell (0, 0) changed at the southern rows: the restored heights alone can put those points under
    /// another mod's water, so they stay raised.
    /// </summary>
    [Fact]
    public void TheRestoredHeightsUnderAWaterFindingAreKept()
    {
        var finding = new TerrainCheckOutcome(WaterOfTheSouthOfTheRaisedCellChanged(), Failure: null);
        var rule = new TestTerrainJudging(default, finding, new HashSet<ExteriorCell> { RaisedCell });
        var scene = new Scene { Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)], StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance), TerrainRule = rule };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(12, 12)], land.FinalHeights[PointAt(12, SouthernRows + 4)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.ForChangedTerrainCheck && kept.Points.Contains(PointAt(12, 12)));
    }

    /// <summary>
    /// The shared rule judges the object that stays in cell (1, 0), counting water changes. An unrelated later mod sets the water of
    /// cell (0, 0) halfway between the earlier ground and the cleaned mod's raised ground, which the cleaned mod did not change. The
    /// accepted height where the ground goes back is the restored one, from a trusted earlier version or as the plan gives it back, so
    /// the ground going back would bring the later mod's water over it and the next run would remove the object. The ground the check
    /// reads stays raised; the ground beyond it goes back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRestoredGroundUnderAnUnrelatedModsWaterStaysWhereTheAcceptedHeightIsTheRestoredOne(bool earlierFromTheBaseGame)
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)],
            StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance) with { CountWaterHeightChanges = true, CountWaterAddedOrRemoved = true },
            EarlierLandPlugin = earlierFromTheBaseGame ? BaseGame : LaterMod.ToPluginName(),
            OtherWater = new()
            {
                [RaisedCell] = [new CellWaterCopy(BaseGame, HasWater: false, OwnLevel: null), new CellWaterCopy(LaterMod.ToPluginName(), HasWater: true, Raised / 2)],
            },
        };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(31, 8)], land.FinalHeights[PointAt(12, 8)]));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.ForChangedTerrainCheck && kept.Points.Contains(PointAt(31, 8)));
    }

    /// <summary>
    /// A check of the object that stays stops over the plan, or over the load order as read after a finding over the plan: everything
    /// the restore writes where the check reads stays, and the failure is reported for the object.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStoppedCheckKeepsWhatItReadsAndIsReported(bool overThePlan)
    {
        var stopped = new TerrainCheckOutcome(Finding: null, "InvalidOperationException: broke");
        var found = new TerrainCheckOutcome(HeightsOfTheSouthOfTheRaisedCellChanged(), Failure: null);
        var rule = new TestTerrainJudging(overThePlan ? default : stopped, overThePlan ? stopped : found, new HashSet<ExteriorCell> { RaisedCell });
        var scene = new Scene { Standing = [new Vector3(ExteriorGrid.CellSize + 1100f, QuarterCentre, Raised)], StandingTerrainCheck = TerrainCheckReaching(TerrainCheckDistance), TerrainRule = rule };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.False(land.Heights.Contains(PointAt(12, 12)));
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.ForChangedTerrainCheck && kept.Points.Contains(PointAt(12, 12)));
        var failure = Assert.Single(result.Failures);
        Assert.Equal((RestoreWhereRemovedParts.StandingTerrainCheck, "InvalidOperationException: broke", FirstStandingTargetKey().ToString()), (failure.Part, failure.Failure, failure.Subject?.Key));
        Assert.Equal(0, result.TerrainFindingsLeft);
    }

    /// <summary>
    /// The object that stays stands 100 units into cell (1, 0); the ground under it, at the east edge of cell (0, 0), stays, and the
    /// restored ground is blended into it over two grid steps. The rule checks 400 units around the object: the blended points it reads
    /// stay raised, which blends the ground further west, until the blend lies beyond what the rule reads, and the loop ends.
    /// </summary>
    [Fact]
    public void ABlendedPointTheRuleCountsIsKeptAndTheLoopEnds()
    {
        var scene = new Scene
        {
            Standing = [new Vector3(ExteriorGrid.CellSize + 100f, QuarterCentre, Raised)],
            StandingTerrainCheck = TerrainCheckReaching(400f),
            EarlierLandPlugin = LaterMod.ToPluginName(),
            Settings = AllOn with { TerrainEdgeBlendDistance = 2 * TerrainGrid.VertexSpacing },
        };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Equal(Raised, land.FinalHeights![PointAt(30, 8)]);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.ForChangedTerrainCheck && kept.Points.Contains(PointAt(30, 8)));
        Assert.InRange(land.FinalHeights[PointAt(28, 8)], 1f, Raised - 1f);
        Assert.Equal(0f, land.FinalHeights[PointAt(12, 8)]);
        Assert.Equal(0, result.TerrainFindingsLeft);
    }

    /// <summary>Two cell records and two LAND records the cleaned mod changed lie at one grid: none of them goes back, and the rest is judged.</summary>
    [Fact]
    public void RecordsSharingAGridNeverGoBack()
    {
        var scene = new Scene
        {
            Cells = [ChangedWater(), ChangedWater() with { Cell = new RecordKey(BaseGame, 0x710) }],
            Lands = [RaisedLand(), RaisedLand() with { LandKey = new RecordKey(BaseGame, 0x601) }],
            Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
        };

        var result = Plan(scene);

        Assert.All(result.Cells, cell => Assert.Equal((NeverRestoredReason.SharesItsGrid, false), (cell.Never, cell.IsRestored)));
        Assert.All(result.Lands, land => Assert.Equal(NeverRestoredReason.SharesItsGrid, land.Never));
        Assert.Equal((2, 2), (result.Cells.Length, result.Lands.Length));
        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Single(result.Objects);
    }

    private static readonly ExteriorCell RaisedCell = new(TestTargets.Space, 0, 0);

    /// <summary>A finding that counts the height change at the vertices of the 16 southern rows of cell (0, 0).</summary>
    private static ObjectOnChangedTerrain HeightsOfTheSouthOfTheRaisedCellChanged() =>
        new(new TargetId(0), LaterMod.ToPluginName(), TerrainChangeKind.Height, [new CountedCell(RaisedCell, RaisedCell, [.. Enumerable.Range(0, SouthernRows * Side)], [], WaterFlipped: [])]);

    /// <summary>A finding that counts the water change at the vertices of the 16 southern rows of cell (0, 0).</summary>
    private static ObjectOnChangedTerrain WaterOfTheSouthOfTheRaisedCellChanged() =>
        new(new TargetId(0), LaterMod.ToPluginName(), TerrainChangeKind.WaterHeight, [new CountedCell(RaisedCell, RaisedCell, [], [], [.. Enumerable.Range(0, SouthernRows * Side)])]);

    private const int SouthernRows = 16;

    /// <summary>
    /// The copy stands on a triangle of the navmesh the cleaned mod moved. The navmesh goes back in the first passes, so actors do not
    /// walk on the copy then; once the house's ground is kept, a later pass takes the navmesh back, it stays changed, and the next
    /// pass loses the copy: the rock still goes back and no copy is written.
    /// </summary>
    [Fact]
    public void ACopyLostInALaterPassStillLetsItsRockGoBack()
    {
        var result = Plan(CopyOnAMovedNavmesh());

        var judged = Assert.Single(result.Objects);
        Assert.Equal(new CloneJudged(CloneRefusal.ActorsWalkOnIt), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
        Assert.Equal(NavmeshGuard.Ground, Assert.Single(result.NavmeshGroups).Refused?.Guard);
        Assert.True(result.Passes >= 3);
    }

    /// <summary>The scene above runs three passes or more, yet the copies are checked too close in one world, built once.</summary>
    [Fact]
    public void TheCopiesAreCheckedTooCloseInOneWorldWhateverThePassCount()
    {
        var worlds = new ConcurrentQueue<CollectedObjects>();

        var result = Plan(CopyOnAMovedNavmesh() with { CheckedWorlds = worlds });

        Assert.True(result.Passes >= 3);
        Assert.Single(worlds.Where(world => world.Targets.Any(target => target.CloneOf is not null)).Distinct(ReferenceEqualityComparer.Instance));
        Assert.True(worlds.Count(world => world.Targets.Any(target => target.CloneOf is not null)) >= 2);
    }

    /// <summary>The copy scene with the copy on a triangle of the navmesh the cleaned mod moved, and the house of a later mod.</summary>
    private static Scene CopyOnAMovedNavmesh()
    {
        var copyPlace = new Vector3(2600, 2600, Raised + RockHalfSize);
        var navmesh = MovedNavmesh() with
        {
            Current = new NavmeshShape(
                TestTargets.TargetName,
                NavmeshCell,
                IsDeleted: false,
                [
                    copyPlace + new Vector3(-5, -5, 0), copyPlace + new Vector3(5, -5, 0), copyPlace + new Vector3(0, 5, 0),
                    new Vector3(550, 550, Raised), new Vector3(650, 550, Raised), new Vector3(600, 650, Raised),
                ],
                [new NavmeshTriangleFact(0, 1, 2, Preferred: false), new NavmeshTriangleFact(3, 4, 5, Preferred: false)],
                [],
                [],
                MapEntry: null),
        };
        return WithAHouseKeepingItsGround(CopyScene(cleanedModsTerrainCheck: null, copyPlace, oldPlace: new Vector3(1500, 1500, 500)) with { Others = [], Navmeshes = [navmesh] });
    }

    private const float TerrainCheckDistance = 1500f;

    /// <summary>The changed-terrain step counting height changes of visible objects on the ground within <paramref name="distance"/> units around them.</summary>
    private static ChangedTerrainSettings TerrainCheckReaching(float distance) =>
        new(
            On: true,
            TerrainCheckedObjects.VisibleObjectsOnTheGround,
            CountHeightChanges: true,
            HeightTolerance: 8f,
            CountTerrainTypeChanges: false,
            MinTerrainTypeChangePercent: 0,
            CountWaterHeightChanges: false,
            WaterHeightTolerance: 8f,
            CountWaterAddedOrRemoved: false,
            MinWaterCoverChangePercent: 0,
            AreaBySize: false,
            AreaMultiplier: 0f,
            AreaByDistance: true,
            AreaDistanceInUnits: distance);

    /// <summary>
    /// As <see cref="AnObjectWhosePositionALaterPluginKeepsIsNotRefusedForGroundChangedUnderIt"/>, but the cleaned mod tilted the rock
    /// or scaled it down, and that goes back around the kept position: the rock's box changes on the raised ground, which an unrelated
    /// plugin raised and the world without the cleaned mod has too.
    /// </summary>
    [Theory]
    [InlineData(PlacedParts.Rotation)]
    [InlineData(PlacedParts.Scale)]
    public void AnObjectWhosePositionALaterPluginKeepsIsNotRefusedWhenATiltOrScaleGoesBackOnGroundAnUnrelatedPluginChanged(PlacedParts part)
    {
        var placementNow = part == PlacedParts.Rotation
            ? new EulerPlacement(OnGroundRaisedLater, new Vector3(0.5f, 0, 0), 1f)
            : new EulerPlacement(OnGroundRaisedLater - new Vector3(0, 0, RockHalfSize / 2), Vector3.Zero, 0.5f);
        var rock = RockKeepingALaterPosition(part, earlier => earlier) with { WhereItIsNow = placementNow };
        var copyNow = RockNow(0, placementNow.Position) with { Rotation = placementNow.Rotation, Scale = placementNow.Scale };

        var judged = Assert.Single(Plan(GroundChangedLater(OnGroundRaisedLater, earlier: 0f, final: RaisedLater) with { Rocks = [rock], Others = [copyNow] }).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A landscape mod after the cleaned mod raised the ground under a rock that stays in its place. The cleaned mod hid the rock, so
    /// it is shown again there, or gave it another model, so its earlier one comes back: either way the rock as it ends is checked
    /// against the ground of the world without the cleaned mod, which has the raised ground too, so it is not refused.
    /// </summary>
    [Theory]
    [InlineData(PlacedParts.Presence)]
    [InlineData(PlacedParts.BaseAndData)]
    public void AnObjectShownAgainOrGettingItsModelBackInItsPlaceIsNotRefusedForGroundOnlyAnUnrelatedPluginChanged(PlacedParts undo)
    {
        var place = RockNowPlace with { Z = RaisedLater };
        var moved = Rock(0, place, place);
        var rock = moved with { Earlier = moved.Earlier with { Undo = undo, Base = undo == PlacedParts.BaseAndData ? Tree.Base : moved.Earlier.Base } };
        var scene = GroundChangedLater(place, earlier: 0f, final: RaisedLater) with
        {
            Rocks = [rock],
            Others = undo == PlacedParts.Presence ? [] : [RockNow(0, place)],
            Presence = undo == PlacedParts.Presence ? Disabled(rock.Key) : new(),
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>A rock the cleaned mod moved and a later plugin moved again to <see cref="OnGroundRaisedLater"/>; only <paramref name="undo"/> goes back.</summary>
    private static RecordChangedByCleanedMod RockKeepingALaterPosition(PlacedParts undo, Func<EarlierVersion, EarlierVersion> earlier)
    {
        var moved = Rock(0, new Vector3(1500, 1500, RockHalfSize), OnGroundRaisedLater);
        return moved with
        {
            Earlier = earlier(moved.Earlier with
            {
                Undo = undo,
                LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Position, LaterMod.ToPluginName()),
            }),
        };
    }

    /// <summary>
    /// A landscape mod after the cleaned mod moved every point of cell (0, 0), whose LAND record the cleaned mod does not override, from
    /// <paramref name="earlier"/> to <paramref name="final"/>; the cleaned mod moved the rock from <paramref name="oldPlace"/>.
    /// </summary>
    private static Scene GroundChangedLater(Vector3 oldPlace, float earlier, float final) => new()
    {
        Rocks = [Rock(0, oldPlace, RockNowPlace)],
        Others = [RockNow(0, RockNowPlace)],
        Lands = [],
        ReadHeights = cell => cell == new ExteriorCell(TestTargets.Space, 0, 0)
            ? new LandHeights([.. Enumerable.Repeat(earlier, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(final, TerrainGrid.VertexCount)])
            : new LandHeights(null, null),
    };

    /// <summary>The LAND record of cell (0, 0) with the base game's flat ground in both versions, for scenes whose terrain goes nowhere.</summary>
    private static LandChangedByCleanedMod FlatLand() =>
        RaisedLand() with { Heights = new LandHeights([.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)]) };

    /// <summary>Each plugin's version of a LAND record painted with a texture of its own.</summary>
    private static TerrainLayers? PaintedPerPlugin(PluginName plugin) => TestTerrainView.PaintedWith(new RecordKey(plugin, 0x300));

    /// <summary><see cref="ChangedWater"/> with the earlier level from a plugin the changed-terrain step does not accept.</summary>
    private static CellChangedByCleanedMod ChangedUntrustedWater() =>
        ChangedWater() with { EarlierWater = new CellWaterCopy(LaterMod.ToPluginName(), HasWater: true, EarlierWater) };
}
