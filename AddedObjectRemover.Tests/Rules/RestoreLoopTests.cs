using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The guards between the kinds that go back where the cleaned mod was removed, repeated until nothing more changes, over in-memory
/// scenes: the cleaned mod raised the base game's flat ground of cell (0, 0) by 80 units and every one of its objects around the cell
/// was removed, so every area there is cleared. Its moved rocks, an edited navmesh, the cell's water level and the ground go back
/// unless what stays keeps them.
/// </summary>
public sealed class RestoreLoopTests
{
    private const float Raised = 80f;
    private const float RockHalfSize = 20f;
    private const float TooCloseDistance = 10f;
    private const float QuarterCentre = ExteriorGrid.CellSize / 4;
    private const float CellCentre = ExteriorGrid.CellSize / 2;
    private const float NavmeshHalfSize = 100f;
    private const float EarlierWater = 50f;
    private const float CleanedModsWater = -100f;
    private const int Side = TerrainGrid.VerticesPerSide;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreLoopShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreloop\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    private static readonly TestStatic House = new(new FormKey(ShapeMod, 0x802), @"test\restoreloop\house.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-100, -100, 0), new Vector3(100, 100, 100))));
    private static readonly TestStatic Tree = new(new FormKey(ShapeMod, 0x803), @"test\restoreloop\tree.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 300))));

    /// <summary>An invisible wall: a mesh with collision that stops characters and nothing to render.</summary>
    private static readonly TestStatic CollisionWall = new(
        new FormKey(ShapeMod, 0x804), @"test\restoreloop\collisionwall.nif", [], NiflySharp.Enums.SkyrimLayer.SKYL_STATIC, new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize)));

    /// <summary>A base with the engine's marker flag and nothing to render, as a collision box primitive has.</summary>
    private static readonly TestStatic MarkerBase = new(
        new FormKey(ShapeMod, 0x805), @"test\restoreloop\markerbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)), IsMarker: true);

    /// <summary>A mist: a mesh drawn only with an effect shader, which blocks no one.</summary>
    private static readonly TestStatic Mist = new(
        new FormKey(ShapeMod, 0x806), @"test\restoreloop\mist.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))), EffectOnly: true);

    /// <summary>A log lying east to west, long enough to reach over several grid points.</summary>
    private static readonly TestStatic Log = new(new FormKey(ShapeMod, 0x807), @"test\restoreloop\log.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-300, -20, 0), new Vector3(300, 20, 40))));

    private const string ShapeDataFolder = "RestoreLoopData";

    private static readonly TestStatic[] Statics = [Block, House, Tree, CollisionWall, MarkerBase, Mist, Log];

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(
        TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE)), ShapeDataFolder, Statics));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, LaterMod.ToPluginName()];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []);
    private static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap: 1f, SupportLostFraction: 0.5f);

    private static readonly WhereRemovedRestoreSettings AllOn = new(
        OtherModsObjects: true,
        Terrain: true,
        Cells: true,
        Navmeshes: true,
        new ClearedArea(SearchRadius: 1000f, MaxStandingSizePercent: 0.1, MaxStandingCountPercent: 0.1),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never));

    private static readonly RecordKey WaterCell = new(BaseGame, 0x700);
    private static readonly RecordKey NavmeshCell = new(BaseGame, 0x701);
    private static readonly RecordKey NavmeshKey = new(BaseGame, 0x702);

    /// <summary>A later mod's house stands on the raised ground over the navmesh's earlier place.</summary>
    private static readonly Vector3 HousePlace = new(1000, 2000, Raised);

    /// <summary>The cleaned mod moved the navmesh from under the house to here.</summary>
    private static readonly Vector3 NavmeshNow = new(2600, 2600, 0);

    private static readonly Vector3 RockNowPlace = new(3500, 1500, Raised + RockHalfSize);

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
        Assert.Equal((true, new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null)), (judged.IsRestored, judged.Cloning));
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
    /// pass loses the copy: the rock stays for its area and no copy is written.
    /// </summary>
    [Fact]
    public void ACopyLostInALaterPassKeepsItsRockForItsArea()
    {
        var result = Plan(CopyOnAMovedNavmesh());

        var judged = Assert.Single(result.Objects);
        Assert.Equal(new CloneJudged(CloneNeed.AreaStillStands, CloneRefusal.ActorsWalkOnIt), judged.Cloning);
        Assert.Equal((false, (AreaVerdict?)AreaVerdict.StillStanding), (judged.IsRestored, judged.KeptForArea));
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

    /// <summary>
    /// The cleaned mod moved a base game rock from <paramref name="oldPlace"/> (by default the cleared south-west quarter of cell
    /// (0, 0), raised over the water levels) to <paramref name="copyPlace"/> (by default its south-east quarter), beside an object of its
    /// own that stays 300 units north, so the rock goes back with a copy left there.
    /// </summary>
    private static Scene CopyScene(ChangedTerrainSettings? cleanedModsTerrainCheck, Vector3? copyPlace = null, Vector3? oldPlace = null)
    {
        var now = copyPlace ?? RockNowPlace;
        var rock = Rock(0, oldPlace ?? new Vector3(1500, 1500, 500), now) with { Key = FirstStandingTargetKey(), Target = new TargetId(RemovedTargets().Count) };
        return new Scene
        {
            Rocks = [rock],
            Standing = [now],
            OwnStanding = [now + new Vector3(0, 300, 0)],
            Bases = TestBaseFacts.PlainScenery,
            CleanedModsTerrainCheck = cleanedModsTerrainCheck,
        };
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

    private static float[] HeightsAll(float height) => [.. Enumerable.Repeat(height, TerrainGrid.VertexCount)];

    /// <summary>The ground as it is: cell (0, 0) at the given height everywhere.</summary>
    private static TerrainHeights GroundAll(float height) => new(cell => cell == new ExteriorCell(TestTargets.Space, 0, 0) ? HeightsAll(height) : null, space => space);

    /// <summary>The base game trusted in full, as the base game always is.</summary>
    private static readonly ModTrustTable BaseGameTrusted = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [], baseGame: [BaseGame]);

    /// <summary>The ground as it is: cell (0, 0) raised as <see cref="RaisedLand"/> makes it.</summary>
    private static TerrainHeights RaisedGround() => GroundAll(Raised);

    /// <summary>A base game navmesh the cleaned mod did not change lies on the raised ground, or far above it.</summary>
    [Theory]
    [InlineData(Raised, true)]
    [InlineData(Raised + 500f, false)]
    public void TheGroundUnderANavmeshLyingOnItAsItIsIsKept(float navmeshHeight, bool kept)
    {
        var square = Square(BaseGame, new Vector3(1500, 1500, navmeshHeight));
        var scene = new Scene { Winning = [.. Enumerable.Range(0, square.Triangles.Length).Select(triangle => new NavmeshWorldTriangle(NavmeshKey, square.CornersOf(triangle)!.Value))] };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(12, 12)]);
        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderNavmesh && points.Points.Contains(PointAt(12, 12))));
    }

    /// <summary>The navmesh's earlier place lies under the house, whose ground the first pass keeps; the next pass takes the navmesh back.</summary>
    [Fact]
    public void ANavmeshGroupIsTakenBackOnceTheGroundUnderItsEarlierPlaceIsKept()
    {
        var scene = WithAHouseKeepingItsGround(new Scene { Navmeshes = [MovedNavmesh()] });

        var result = Plan(scene);

        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Ground, NavmeshKey, Fault: null), group.Refused);
        Assert.Null(group.Write);
    }

    /// <summary>The rock goes back into the navmesh's place now, which is free while the navmesh goes back and not once it is taken back.</summary>
    [Fact]
    public void ARockGoingBackIntoANavmeshThatIsTakenBackIsTakenBackToo()
    {
        var result = Plan(RockInTheNavmeshTakenBack());

        var rock = Assert.Single(result.Objects);
        Assert.Equal((new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, NavmeshKey, Failure: null), false), (rock.Refused, rock.IsRestored));
    }

    /// <summary>
    /// A mist of the cleaned mod that stays stands across the line between the water level before the cleaned mod and its lower one.
    /// An object at the water line keeps no level, so the water still goes back and the mist then stands out of it.
    /// </summary>
    [Fact]
    public void AStandingMistOfTheCleanedModAtTheWaterLineKeepsNoWaterLevel()
    {
        var scene = new Scene { Standing = [new Vector3(CellCentre + 300, CellCentre, 0)], StandingModel = Mist.Base, Lands = [], Cells = [ChangedWater()] };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Equal(((RecordKey?)null, true), (cell.WaterKeptBy, cell.IsRestored));
    }

    /// <summary>
    /// Two base game rocks the cleaned mod moved go back to old places 30 units apart; the first keeps a turn a later plugin gave it,
    /// so the two overlap once back. The restore does not clean anything between other mods' objects: both go back.
    /// </summary>
    [Fact]
    public void TwoRocksGoingBackThatOverlapOnceBackBothGoBack()
    {
        var first = Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace);
        var turned = first with
        {
            WhereItIsNow = new EulerPlacement(RockNowPlace, new Vector3(0, 0, 0.5f), 1f),
            Earlier = first.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Rotation, LaterMod.ToPluginName()) },
        };
        var secondNow = RockNowPlace + new Vector3(0, 500, 0);
        var scene = new Scene
        {
            Rocks = [turned, Rock(1, new Vector3(1530, 1500, RockHalfSize), secondNow)],
            Others = [RockNow(0, RockNowPlace) with { Rotation = new Vector3(0, 0, 0.5f) }, RockNow(1, secondNow)],
            Lands = [],
        };

        Assert.All(Plan(scene).Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
    }

    /// <summary>
    /// A later mod's crate stands between the water level before the cleaned mod and the cleaned mod's lower one, or far above both:
    /// another mod's object at the water line keeps no level, so the level goes back with the cell's other parts.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(500f)]
    public void AnObjectBetweenTheTwoWaterLevelsKeepsNoLevelAndTheLevelGoesBackWithTheCellsOtherParts(float crateHeight)
    {
        var crate = LaterObject(10, Block.Base, new Vector3(CellCentre + 300, CellCentre, crateHeight));
        var scene = new Scene { Others = [crate], Lands = [], Cells = [ChangedWater()] };

        var cell = Assert.Single(Plan(scene).Cells);

        Assert.Null(cell.WaterKeptBy);
        Assert.Equal([CellPart.Music, CellPart.WaterLevel], cell.Undone.Select(part => part.Name.Part));
        Assert.True(cell.IsRestored);
    }

    /// <summary>
    /// A base game rock the cleaned mod moved is refused next to a later mod's crate at its old place, so it stays where the cleaned mod
    /// put it: across the line between the water level before the cleaned mod and its lower one, or far above both. Across the line it
    /// keeps the cleaned mod's level, although no changed-terrain check counts water; above both, the level goes back.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(500f, false)]
    public void ARefusedRockAtTheWaterLineKeepsTheCleanedModsWaterLevel(float rockNowHeight, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, 500f);
        var rockNow = new Vector3(1500, 2500, rockNowHeight);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, oldPlace, rockNow)], Others = [crate, RockNow(0, rockNow)], Lands = [], Cells = [ChangedWater()] };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(PlacedGuard.TooCloseToWhatStays, rock.Refused?.Guard);
        var cell = Assert.Single(result.Cells);
        Assert.Equal(kept ? (rock.Change.Key, WaterKeptReason.ObjectAtWaterLine) : (null, WaterKeptReason.NavmeshAtWaterLine), (cell.WaterKeptBy, cell.WaterKeptFor));
        Assert.True(cell.IsRestored);
    }

    /// <summary>
    /// The rock of <see cref="CopyScene"/> goes back with a copy left where the cleaned mod put it, across the water line or above both
    /// levels, and the cleaned mod's own changed-terrain check is off. Across the line the copy keeps the cleaned mod's level, named by
    /// its original; above both, the level goes back.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(Raised + RockHalfSize, false)]
    public void ACopyAtTheWaterLineKeepsTheCleanedModsWaterLevel(float copyHeight, bool kept)
    {
        var scene = CopyScene(cleanedModsTerrainCheck: null, copyPlace: RockNowPlace with { Z = copyHeight }) with { Cells = [ChangedWater()], Lands = [] };

        var result = Plan(scene);

        var judged = Assert.Single(result.Objects);
        Assert.Equal((true, new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null)), (judged.IsRestored, judged.Cloning));
        var cell = Assert.Single(result.Cells);
        Assert.Equal(
            kept ? (FirstStandingTargetKey(), WaterKeptReason.ObjectAtWaterLine) : (null, WaterKeptReason.NavmeshAtWaterLine), (cell.WaterKeptBy, cell.WaterKeptFor));
    }

    /// <summary>
    /// The rock goes back across the water line. Switched on, the cell's water level goes back with it, so the rock goes back. With the
    /// cell settings switched off the cleaned mod's water level stays as if every cell were kept, and with a navmesh that stays changed
    /// at the water line it stays too: the rock would go back into the cleaned mod's water line, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ARockGoingBackAcrossAWaterLineIsRefusedOnlyWhenTheLevelStays(bool cellsOn, bool navmeshAtTheLine, bool baseGameTrusted)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1000, 1000, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [ChangedWater()],
            Navmeshes = navmeshAtTheLine ? [NavmeshInTheWaterCell(NavmeshNow, laterWinnerAt: null)] : [],
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Settings = AllOn with { Cells = cellsOn, Navmeshes = !navmeshAtTheLine },
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        var levelStays = !cellsOn || navmeshAtTheLine;
        Assert.Equal(levelStays ? (false, PlacedGuard.ArrivesAtWaterLine) : (true, null), (rock.IsRestored, rock.Refused?.Guard));
        if (navmeshAtTheLine) Assert.Equal(NavmeshKey, Assert.Single(result.Cells).WaterKeptBy);
    }

    /// <summary>
    /// The rock goes back across the line between the water level before the cleaned mod and the cleaned mod's, which a later plugin
    /// set again after it. That level is the later plugin's (decision 33) and never goes back, but it refuses nothing: the rock goes
    /// back, cell settings on or off.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoingBackAcrossAWaterLevelALaterPluginSetAgainGoesBack(bool cellsOn)
    {
        var setAgain = ChangedWater() with
        {
            Parts = [new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()), new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.LeftToLaterPlugin(LaterMod.ToPluginName()))],
            CurrentWater = new CellWaterCopy(LaterMod.ToPluginName(), HasWater: true, CleanedModsWater),
        };
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1000, 1000, 0), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Lands = [],
            Cells = [setAgain],
            Settings = AllOn with { Cells = cellsOn },
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
    }

    /// <summary>
    /// The rock's old place lies on the flat ground the cleaned mod raised, which goes back when the terrain is switched on and stays
    /// when off: then the rock would go back into the cleaned mod's ground, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ARockGoingBackOntoGroundTheCleanedModRaisedIsRefusedOnlyWhenTheGroundStays(bool terrainOn, bool baseGameTrusted)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Ground = RaisedGround(),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Settings = AllOn with { Terrain = terrainOn },
        };

        var rock = Assert.Single(Plan(scene).Objects);

        Assert.Equal(terrainOn ? (true, null) : (false, PlacedGuard.ArrivesInGround), (rock.IsRestored, rock.Refused?.Guard));
    }

    /// <summary>
    /// The cleaned mod lowered cell (0, 0) from 80 units to flat ground, which stays while the terrain is switched off, and the rock's old
    /// place stood on the ground before. Going back, it would float over the cleaned mod's ground: a base game rock, trusted in full, is
    /// pulled down onto it or removed where it arrives; an untrusted one is refused.
    /// </summary>
    [Theory]
    [InlineData(false, OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(true, OtherModsObjectsHandling.PullObjectToTheGround)]
    [InlineData(true, OtherModsObjectsHandling.RemoveFloatingObject)]
    public void ARockGoingBackOverGroundTheCleanedModLoweredIsPulledDownRemovedOrRefused(bool baseGameTrusted, OtherModsObjectsHandling handling)
    {
        var rockNow = RockNowPlace with { Z = RockHalfSize };
        var scene = new Scene
        {
            Rocks = [Rock(0, new Vector3(1500, 1500, Raised + RockHalfSize), rockNow)],
            Others = [RockNow(0, rockNow)],
            Lands = [RaisedLand() with { Heights = new LandHeights(HeightsAll(Raised), HeightsAll(0f)) }],
            Ground = GroundAll(0f),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
            Support = Support with { Handling = handling },
            Settings = AllOn with { Terrain = false },
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        if (!baseGameTrusted)
        {
            Assert.Equal((false, PlacedGuard.ArrivesWithoutSupport), (rock.IsRestored, rock.Refused?.Guard));
            return;
        }
        Assert.Equal((true, null), (rock.IsRestored, rock.Refused));
        var followed = Assert.Single(result.FollowUp.Others);
        Assert.Equal(rock.Change.Key, followed.Outcome.Object.Key);
        if (handling == OtherModsObjectsHandling.RemoveFloatingObject) Assert.True(followed.Outcome.IsRemoved);
        else Assert.Equal((OtherModsObjectFate.PulledDown, RockHalfSize), (followed.Outcome.Fate, MathF.Round(followed.Outcome.NewPosition.Z)));
    }

    /// <summary>
    /// A block the cleaned mod moved, which no round removes, stays on the raised ground and keeps the ground under it (decision 29), and
    /// a base game rock the cleaned mod moved goes back beside it, onto the slope from the kept point down to the flat ground going back:
    /// it would go back into the cleaned mod's ground, so it is refused, trusted or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockGoingBackBesideACleanedModsObjectThatKeepsItsGroundIsRefused(bool baseGameTrusted)
    {
        var standing = new Vector3(12 * TerrainGrid.VertexSpacing, 12 * TerrainGrid.VertexSpacing, Raised + RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, standing with { X = standing.X + 60f, Z = RockHalfSize }, RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Standing = [standing],
            Ground = RaisedGround(),
            Trust = baseGameTrusted ? BaseGameTrusted : Trust,
        };

        var result = Plan(scene);

        var land = Assert.Single(result.Lands);
        Assert.Contains(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectThatStays && kept.Points.Contains(PointAt(12, 12)));
        Assert.Equal((false, PlacedGuard.ArrivesInGround), (Assert.Single(result.Objects).IsRestored, result.Objects[0].Refused?.Guard));
    }

    /// <summary>
    /// The house keeps its ground, which takes the navmesh back, which takes the rock back, which keeps the ground under it: each pass
    /// but the last adds at least one refusal, protected point or kept water level, so the passes stay within one more than all that
    /// the final state holds.
    /// </summary>
    [Fact]
    public void TheLoopEndsWithinOneMoreThanTheRefusalsProtectedPointsAndKeptLevelsItEndsWith()
    {
        var result = Plan(RockInTheNavmeshTakenBack());

        var added = result.Objects.Count(judged => judged.Refused is not null)
            + result.NavmeshGroups.Count(group => group.Refused is not null)
            + result.Lands.Sum(land => land.Kept.Sum(kept => kept.Points.Count))
            + result.Cells.Count(cell => cell.WaterKeptBy is not null);
        Assert.InRange(result.Passes, 3, added + 1);
    }

    /// <summary>
    /// The cleaned mod disabled the rock and moved it; a later mod's crate stands next to its old place, so it stays disabled and stands
    /// nowhere: the ground where it stood moved keeps no protection, and a navmesh whose earlier triangles pass there goes back.
    /// </summary>
    [Fact]
    public void ARefusedRockTheCleanedModDisabledKeepsNoGroundAndKeepsNoNavmeshOutOfItsPlace()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var navmesh = MovedNavmesh() with { Earlier = Square(BaseGame, rockNow) };
        var rock = Rock(0, oldPlace, rockNow, removedNow: true);
        var scene = new Scene { Rocks = [rock], Others = [crate], Navmeshes = [navmesh], Presence = Disabled(rock.Key) };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(0f, land.FinalHeights![PointAt(12, 20)]);
        Assert.DoesNotContain(land.Kept, kept => kept.Reason == TerrainKeptReason.UnderObjectKeptAsChanged);
        Assert.NotNull(Assert.Single(result.NavmeshGroups).Write);
    }

    /// <summary>
    /// A later mod's crate refuses the rock, which stands now in the navmesh's earlier place: the group is refused in the pass after,
    /// the first in which the rock stays.
    /// </summary>
    [Fact]
    public void ARockRefusedInOnePassRefusesTheNavmeshGroupWhoseEarlierPlaceItStandsInInTheNext()
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = Rock(0, oldPlace, rockNow);
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, present: true)],
            Navmeshes = [MovedNavmesh() with { Earlier = Square(BaseGame, rockNow) }],
            Lands = [],
        };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.TooCloseToWhatStays, Assert.Single(result.Objects).Refused?.Guard);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Object, rock.Key, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
    }

    /// <summary>
    /// The trial write meets a record the game library cannot parse: the step stops with that failure and judges nothing, so the run
    /// goes on and the patch is written without anything of this step.
    /// </summary>
    [Fact]
    public void AMalformedRecordTheTrialWriteMeetsStopsTheStepWithItsFailureAndJudgesNothing()
    {
        var scene = new Scene { Navmeshes = [MovedNavmesh()], FindFault = _ => throw new MalformedRecordException(new ArgumentOutOfRangeException()) };

        var result = Plan(scene);

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
        Assert.Empty(result.Objects);
        Assert.Empty(result.NavmeshGroups);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(RestoreWhereRemovedParts.Step, failure.Part);
        Assert.StartsWith(nameof(MalformedRecordException), failure.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door the cleaned mod disabled and moved. The door goes back unless a later mod's
    /// crate stands next to its old place; then it stays disabled, and the navmesh group naming it stays too.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANavmeshGroupNamingADoorTheCleanedModDisabledGoesBackOnlyWithTheDoor(bool crateAtTheDoor)
    {
        var doorOldPlace = new Vector3(3000, 500, RockHalfSize);
        var door = Rock(1, doorOldPlace, doorOldPlace + new Vector3(0, 300, 0), removedNow: true);
        var navmesh = MovedNavmesh() with { Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door.Key)] } };
        List<OtherObject> crates = crateAtTheDoor ? [LaterObject(10, Block.Base, doorOldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0))] : [];
        var scene = new Scene { Rocks = [door], Others = crates, Navmeshes = [navmesh], Lands = [], Presence = Disabled(door.Key) };

        var result = Plan(scene);

        Assert.Equal(crateAtTheDoor, Assert.Single(result.Objects).Refused is not null);
        var group = Assert.Single(result.NavmeshGroups);
        Assert.Equal(crateAtTheDoor ? new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null) : null, group.Refused);
    }

    /// <summary>
    /// As above, but a later patch carries the cleaned mod's disable of the door forward, so no part of the door goes back and the run
    /// holds no placed record fact of it: the navmesh's door facts say it is removed, it stays disabled, and the group naming it stays.
    /// </summary>
    [Fact]
    public void ANavmeshGroupNamingADoorALaterPluginKeepsDisabledIsRefused()
    {
        var door = new RecordKey(BaseGame, 0x950);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), Earlier: null, Current: null, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Navmeshes = [navmesh], Lands = [], Presence = Disabled(door) });

        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal([door], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door the cleaned mod disabled and moved, and the door's position goes back. Its
    /// presence does not go back (a later patch carries the disable forward), or goes back to a version that was disabled too: either
    /// way the door ends disabled, and the navmesh group naming it stays.
    /// </summary>
    [Theory]
    [InlineData(PlacedParts.Position, false)]
    [InlineData(PlacedParts.Position | PlacedParts.Presence, true)]
    public void ANavmeshGroupNamingADoorThatEndsDisabledIsRefusedThoughTheDoorGoesBack(PlacedParts undo, bool removedBefore)
    {
        var doorOldPlace = new Vector3(3000, 500, RockHalfSize);
        var moved = Rock(1, doorOldPlace, doorOldPlace + new Vector3(0, 300, 0), removedNow: true);
        var door = moved with { Earlier = moved.Earlier with { Undo = undo }, PresenceBefore = new PlacedPresence(removedBefore, Parent: null, OppositeOfParent: false) };
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(0, door.Key)] },
            Doors = [new DoorLinkChange(door.Key, new PartOutcome.Unchanged(), Earlier: null, Current: null, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Rocks = [door], Navmeshes = [navmesh], Lands = [], Presence = Disabled(door.Key) });

        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null), Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal([door.Key], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door that is enabled with a parent, or with the opposite of it. A base game parent
    /// the cleaned mod disabled stays disabled. The door ends disabled, and the group naming it stays, when the parent is disabled
    /// and the door follows it, or the parent stands and the door is the opposite of it.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void ANavmeshGroupNamingADoorEnabledWithAParentGoesBackOnlyWhileTheDoorEndsEnabled(bool parentDisabled, bool doorOppositeOfParent, bool refused)
    {
        var door = new RecordKey(BaseGame, 0x950);
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), link, link, InDeletedCell: false)],
        };

        var result = Plan(new Scene { Navmeshes = [navmesh], Lands = [], Presence = FollowingAParent(door, parentDisabled, doorOppositeOfParent) });

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null) : null, Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal(refused ? [door] : [], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>
    /// A later mod's house stands over the navmesh's earlier place. It is enabled with a parent that stands, or with a base game parent
    /// that stays disabled, which takes the house with it, so the house stands in no navmesh.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AnObjectEnabledWithAParentRefusesANavmeshGroupOnlyWhileTheParentIsEnabled(bool parentDisabled, bool refused)
    {
        var house = LaterObject(10, House.Base, HousePlace with { Z = -10 });
        var scene = new Scene { Others = [house], Navmeshes = [MovedNavmesh()], Lands = [], Presence = FollowingAParent(house.Key, parentDisabled) };

        var group = Assert.Single(Plan(scene).NavmeshGroups);

        Assert.Equal(refused ? NavmeshGuard.Object : null, group.Refused?.Guard);
    }

    /// <summary>As the first of the refused rocks: a later mod's crate refuses the rock, which is enabled with a parent that stands or one that stays disabled.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ARefusedRockEnabledWithAParentKeepsTheGroundUnderItOnlyWhileTheParentIsEnabled(bool parentDisabled, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var rock = Rock(0, oldPlace, rockNow);
        var scene = new Scene { Rocks = [rock], Others = [crate, RockNow(0, rockNow)], Presence = FollowingAParent(rock.Key, parentDisabled) };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectKeptAsChanged));
    }

    /// <summary>
    /// Rock P was disabled before the cleaned mod, which enabled and moved it. An object of another plugin, which the cleaned mod
    /// moved, is enabled with P and stands in the middle of the cleared cell on the raised ground. P's restore disables it again and
    /// the object with it, so the ground under it goes back; a later mod's crate beside P's old place refuses P, which stays enabled,
    /// and the object stands, so its ground stays.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TheGroundUnderAnObjectEnabledWithARockIsKeptOnlyWhenARefusalKeepsTheRockEnabled(bool crateRefusesTheRock, bool kept)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = WithPresenceRestoredToDisabled(Rock(0, oldPlace, rockNow));
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, crateRefusesTheRock)],
            Standing = [new Vector3(CellCentre, CellCentre, Raised)],
            Presence = new Dictionary<RecordKey, PlacedPresence> { [FirstStandingTargetKey()] = new(Removed: false, rock.Key, OppositeOfParent: false) },
        };

        var result = Plan(scene);

        Assert.Equal(crateRefusesTheRock ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(result.Objects).Refused?.Guard);
        var land = Assert.Single(result.Lands);
        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectThatStays && points.Points.Contains(PointAt(16, 16))));
        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(16, 16)]);
    }

    /// <summary>
    /// The navmesh's earlier door triangle names a door that is the opposite of rock P, which was disabled before the cleaned mod.
    /// P's restore disables it, which enables the door, so the group naming the door goes back; a later mod's crate beside P's old
    /// place refuses P, which stays enabled, and the door stays disabled with it, so the group is refused in the pass after.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANavmeshGroupNamingTheOppositeOfARockIsRefusedInThePassAfterTheRockIsRefused(bool crateRefusesTheRock)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var rockNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var rock = WithPresenceRestoredToDisabled(Rock(0, oldPlace, rockNow));
        var door = new RecordKey(BaseGame, 0x950);
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, door)] },
            Doors = [new DoorLinkChange(door, new PartOutcome.Unchanged(), link, link, InDeletedCell: false)],
        };
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, rockNow), .. CrateBesideTheRock(oldPlace, crateRefusesTheRock)],
            Navmeshes = [navmesh],
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence> { [door] = new(Removed: false, rock.Key, OppositeOfParent: true) },
        };

        var result = Plan(scene);

        Assert.Equal(crateRefusesTheRock ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(result.Objects).Refused?.Guard);
        Assert.Equal(crateRefusesTheRock ? new NavmeshRefusal(NavmeshGuard.Door, door, Fault: null) : null, Assert.Single(result.NavmeshGroups).Refused);
        Assert.Equal(crateRefusesTheRock ? [door] : [], result.DoorsLeftRemoved.ToList());
    }

    /// <summary>A later mod's crate stands next to the rock's old place when <paramref name="present"/>, so the rock is refused.</summary>
    private static List<OtherObject> CrateBesideTheRock(Vector3 oldPlace, bool present) =>
        present ? [LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0))] : [];

    /// <summary>The base game disabled the rock, and the cleaned mod enabled it and moved it: its restore moves it back and disables it again.</summary>
    private static RecordChangedByCleanedMod WithPresenceRestoredToDisabled(RecordChangedByCleanedMod rock) =>
        rock with { Earlier = rock.Earlier with { Undo = PlacedParts.Position | PlacedParts.Presence }, PresenceBefore = new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false) };

    private const string StoppedCheck = "InvalidOperationException: broke";

    /// <summary>A target of the cleaned mod's run that stays, judged by the too-close step, away from the rocks' old places.</summary>
    private static readonly Vector3 StandingPlace = new(3000, 3000, RockHalfSize);

    private static readonly Vector3 RockAOldPlace = new(1500, 1500, RockHalfSize);
    private static readonly Vector3 RockANow = new(1500, 2500, Raised + RockHalfSize);
    private static readonly Vector3 RockBOldPlace = new(1500, 3800, RockHalfSize);
    private static readonly Vector3 RockBNow = new(500, 3500, Raised + RockHalfSize);

    /// <summary>A later mod's object set to the opposite of its enable parent, at the given place.</summary>
    private static (OtherObject Object, KeyValuePair<RecordKey, PlacedPresence> Presence) OppositeOf(RecordKey parent, int index, Vector3 position)
    {
        var placed = LaterObject(index, Block.Base, position);
        return (placed, KeyValuePair.Create(placed.Key, new PlacedPresence(Removed: false, parent, OppositeOfParent: true)));
    }

    /// <summary>
    /// A later mod's object X is the opposite of rock A, which the base game disabled and the cleaned mod enabled: A's restore disables
    /// it again, so X stands then, beside a target that stays and that the too-close step judges. The next patcher would remove the
    /// target, so A is refused, naming the target, and X stays disabled. When another later object already stands too close to the
    /// target, the target is removed anyway and A goes back.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockWhoseRestoreEnablesAnObjectTooCloseToATargetThatStaysIsRefused(bool alreadyTooClose)
    {
        var rock = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var x = OppositeOf(rock.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var scene = new Scene
        {
            Rocks = [rock],
            Others = [RockNow(0, RockANow), x.Object, .. alreadyTooClose ? [LaterObject(12, Block.Base, StandingPlace - new Vector3(25, 0, 0))] : new List<OtherObject>()],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence]),
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(alreadyTooClose ? null : new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, FirstStandingTargetKey(), Failure: null), judged.Refused);
    }

    /// <summary>
    /// As <see cref="ARockWhoseRestoreEnablesAnObjectTooCloseToATargetThatStaysIsRefused"/>, but X is the opposite of rock A, which
    /// follows rock B, and both A's and B's restores disable them: X stands when either goes back, so leaving out one alone does not
    /// remove it. Both are refused, one pass after the other.
    /// </summary>
    [Fact]
    public void RocksThatEachEnableAnObjectTooCloseToATargetThatStaysAreAllRefused()
    {
        var rockB = WithPresenceRestoredToDisabled(Rock(1, RockBOldPlace, RockBNow));
        var rockA = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var x = OppositeOf(rockA.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var scene = new Scene
        {
            Rocks = [rockA, rockB],
            Others = [RockNow(0, RockANow), RockNow(1, RockBNow), x.Object],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence, KeyValuePair.Create(rockA.Key, new PlacedPresence(Removed: false, rockB.Key, OppositeOfParent: false))]),
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, FirstStandingTargetKey(), Failure: null), judged.Refused));
        Assert.Equal(2, result.Objects.Length);
    }

    /// <summary>
    /// Rock A's restore enables X beside the target that stays, and rock B's restore enables Y far from it. The target's too-close
    /// check stops, with nothing going back too: A, whose X lies within the target's zones, is refused with the failure; B goes back.
    /// </summary>
    [Fact]
    public void AStoppedCheckOfATargetThatStaysRefusesOnlyTheRocksEnablingObjectsWithinItsReach()
    {
        var rockA = WithPresenceRestoredToDisabled(Rock(0, RockAOldPlace, RockANow));
        var rockB = WithPresenceRestoredToDisabled(Rock(1, RockBOldPlace, RockBNow));
        var x = OppositeOf(rockA.Key, 11, StandingPlace + new Vector3(25, 0, 0));
        var y = OppositeOf(rockB.Key, 12, new Vector3(3000, 500, RockHalfSize));
        var scene = new Scene
        {
            Rocks = [rockA, rockB],
            Others = [RockNow(0, RockANow), RockNow(1, RockBNow), x.Object, y.Object],
            Standing = [StandingPlace],
            StandingTooClose = AllOn.TooClose,
            TooCloseStopsFor = FirstStandingTargetKey(),
            Lands = [],
            Presence = new Dictionary<RecordKey, PlacedPresence>([x.Presence, y.Presence]),
        };

        var result = Plan(scene);

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, By: null, StoppedCheck), null],
            result.Objects.OrderBy(judged => judged.Change.Key, RecordKeyOrder.Comparer).Select(judged => judged.Refused));
    }

    /// <summary>One more pass from the loop's end state adds nothing: the end state is a fixed point.</summary>
    [Fact]
    public void OneMorePassFromTheEndStateAddsNothing()
    {
        var planned = WhereRemovedRestorer.BuildPlans(InputOf(SceneOfSeveralPasses()));
        var guards = RestoreGuardsBuilder.Build(planned.Inputs, planned.Plans);
        var loop = new RestoreLoop(guards.Placed, guards.Terrain, guards.LoopInputs, () => TimeSpan.Zero);

        var (final, _, passes, _, _) = loop.Run(planned.Plans.Start);
        var (again, _, passesAgain, _, _) = loop.Run(final);

        Assert.True(passes > 1);
        Assert.Equal((1, final.Count), (passesAgain, again.Count));
        Assert.Equal(final.Placed, again.Placed);
        Assert.Equal(final.Clones, again.Clones);
    }

    /// <summary>A rock of the cleaned mod at <see cref="StandingPlace"/>, judged in a world of later mods' objects.</summary>
    private static (TestPlacedGuardRules Rules, CollectedObjects World, TargetObject Target) RuleWorld(params Vector3[] others)
    {
        var target = StandingTarget(0, StandingPlace);
        var world = TestScenes.CreateWorld([target], [.. others.Select((position, index) => LaterObject(index, Block.Base, position) with { Id = new OtherId(index) })]);
        return (TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), threads: 1), world, target);
    }

    [Fact]
    public void TheEveryClashQueryFindsEveryCauseTheObjectIsTooCloseToButThoseLeftOut()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0), StandingPlace + new Vector3(500, 0, 0), StandingPlace - new Vector3(25, 0, 0));
        var rule = rules.TooCloseToWhatStays;

        Assert.Equal(new TooCloseClashes([new OtherId(0), new OtherId(2)], Failure: null), rule.FindEveryClash(world, [], AllOn.TooClose, target), TooCloseClashesComparer);
        Assert.Equal(new TooCloseClashes([new OtherId(2)], Failure: null), rule.FindEveryClash(world, [new OtherId(0)], AllOn.TooClose, target), TooCloseClashesComparer);
    }

    [Fact]
    public void TheEveryClashQueryNamesTheFailureOfACheckThatStops()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0));
        var rule = rules.TooCloseRule((checkedWorld, caches, excluded, settings) => new StoppingForRule(rules.TooClose(checkedWorld, caches, excluded, settings), target.Key));

        Assert.Equal(new TooCloseClashes([], StoppedCheck), rule.FindEveryClash(world, [], AllOn.TooClose, target), TooCloseClashesComparer);
    }

    /// <summary>The zone reach holds the distance zone around the box, and the reach queries find what it overlaps, and every placed NPC.</summary>
    [Fact]
    public void TheReachQueriesFindWhatTheZonesReach()
    {
        var (rules, world, target) = RuleWorld(StandingPlace + new Vector3(25, 0, 0), StandingPlace + new Vector3(500, 0, 0));
        var rule = rules.TooCloseToWhatStays;
        var far = StandingTarget(1, StandingPlace + new Vector3(0, 2000, 0));
        var npc = world.OtherModObjects[1] with { Base = Block.Base with { Kind = BaseLinkKind.Npc } };

        var reach = rule.ZoneReachOf(target, AllOn.TooClose);

        Assert.True(reach.Contains(StandingPlace + new Vector3(RockHalfSize + TooCloseDistance)) && reach.Contains(StandingPlace - new Vector3(RockHalfSize + TooCloseDistance)));
        Assert.False(reach.Contains(StandingPlace + new Vector3(500, 0, 0)));
        Assert.Equal([0], rule.FindObjectsReaching([(target, AllOn.TooClose), (far, AllOn.TooClose)], [world.OtherModObjects[0]]).ToList());
        Assert.Equal([0, 1], rule.FindObjectsReaching([(target, AllOn.TooClose), (far, AllOn.TooClose)], [npc]).ToList());
        Assert.Equal([0], rule.FindOthersReached([(target, AllOn.TooClose)], [.. world.OtherModObjects]).ToList());
        Assert.Equal([0, 1], rule.FindOthersReached([(target, AllOn.TooClose)], [world.OtherModObjects[0], npc]).ToList());
    }

    private static readonly IEqualityComparer<TooCloseClashes> TooCloseClashesComparer =
        EqualityComparer<TooCloseClashes>.Create((one, other) => one!.Others.SequenceEqual(other!.Others) && one.Failure == other.Failure);

    /// <summary>The winning presence of a record enabled with a parent, which a base game plugin's disable keeps disabled when <paramref name="parentDisabled"/>.</summary>
    private static Dictionary<RecordKey, PlacedPresence> FollowingAParent(RecordKey record, bool parentDisabled, bool oppositeOfParent = false)
    {
        var parent = new RecordKey(BaseGame, 0x951);
        var presence = parentDisabled ? Disabled(parent) : new Dictionary<RecordKey, PlacedPresence>();
        presence.Add(record, new PlacedPresence(Removed: false, parent, oppositeOfParent));
        return presence;
    }

    /// <summary>
    /// The cleaned mod moved the base game navmesh and added one of its own, which the patch sinks when it goes back. A later plugin's
    /// navmesh info map, not its navmesh record, merges the base game navmesh's entry with the added one, so the added one stays.
    /// </summary>
    [Fact]
    public void AnAddedNavmeshThatAChangedNavmeshsWinningMapEntryMergesWithStays()
    {
        var added = MovedNavmesh() with
        {
            Navmesh = new RecordKey(TestTargets.TargetName, 0x703),
            Case = NavmeshCase.Added,
            Geometry = new PartOutcome.Unchanged(),
            Presence = new PartOutcome.Undo(),
            Earlier = null,
        };
        var map = new RecordKey(LaterMod.ToPluginName(), 0x704);
        var moved = MovedNavmesh() with { WinningMapEntry = new NavmeshMapEntryFact(LaterMod.ToPluginName(), map, [added.Navmesh], PreferredMerges: [], LinkedDoors: []) };

        var result = Plan(new Scene { Navmeshes = [moved, added], Lands = [] });

        var group = Assert.Single(result.NavmeshGroups, group => group.Members.Contains(added.Navmesh));
        Assert.Equal(new NavmeshRefusal(NavmeshGuard.MapEntry, NavmeshKey, Fault: null), group.Refused);
    }

    /// <summary>
    /// The cleaned mod stores the worldspace's default water height 0 as the cell's own, with the cells switched off: the navmesh
    /// group of the cell holding the navmesh is not refused for the water level that stays.
    /// </summary>
    [Fact]
    public void ANavmeshGroupInACellThatStoresTheDefaultWaterLevelAsItsOwnGoesBack()
    {
        var scene = new Scene { Navmeshes = [MovedNavmesh()], Lands = [], Cells = [WaterSetToTheDefault(NavmeshCell)], Settings = AllOn with { Cells = false } };

        Assert.NotNull(Assert.Single(Plan(scene).NavmeshGroups).Write);
    }

    /// <summary>With the navmeshes switched off, the cleaned mod's navmesh stays; it lies in the water cell between the two levels.</summary>
    [Fact]
    public void WithTheNavmeshesSwitchedOffAChangedNavmeshAcrossTheWaterLineKeepsTheCleanedModsLevel()
    {
        var scene = new Scene { Navmeshes = [NavmeshInTheWaterCell(NavmeshNow, laterWinnerAt: null)], Lands = [], Cells = [ChangedWater()], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(NavmeshKey, Assert.Single(Plan(scene).Cells).WaterKeptBy);
    }

    /// <summary>
    /// The cleaned mod's version of the navmesh lies far above both water levels, but a later plugin's version, which wins, lies between
    /// them; the later version keeps the cleaned mod's level.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWaterLineIsKeptByTheTrianglesOfTheNavmeshsWinningVersion(bool laterWinnerAtTheWaterLine)
    {
        var above = NavmeshNow with { Z = 500 };
        var navmesh = NavmeshInTheWaterCell(above, laterWinnerAt: laterWinnerAtTheWaterLine ? NavmeshNow : null);
        var scene = new Scene { Navmeshes = [navmesh], Lands = [], Cells = [ChangedWater()], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(laterWinnerAtTheWaterLine ? NavmeshKey : null, Assert.Single(Plan(scene).Cells).WaterKeptBy);
    }

    /// <summary>The cleaned mod changed only the navmesh's map entry, so its triangles lie where they did before; the rock goes back into them.</summary>
    [Fact]
    public void ARockGoingBackIntoANavmeshWhoseOnlyChangeIsItsMapEntryIsNotRefused()
    {
        var entryOnly = MovedNavmesh() with { Geometry = new PartOutcome.Unchanged(), MapEntry = new PartOutcome.Undo(), Earlier = Square(BaseGame, NavmeshNow) };
        var scene = new Scene
        {
            Rocks = [Rock(0, NavmeshNow + new Vector3(0, 0, RockHalfSize / 2), RockNowPlace)],
            Others = [RockNow(0, RockNowPlace)],
            Navmeshes = [entryOnly],
            Lands = [],
            Settings = AllOn with { Navmeshes = false },
        };

        Assert.Null(Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>With the navmeshes switched off, the cleaned mod's navmesh stays where it moved it, so a rock going back into it is refused.</summary>
    [Fact]
    public void WithTheNavmeshesSwitchedOffARockGoingBackIntoAChangedNavmeshIsRefused()
    {
        var scene = RockInTheNavmeshTakenBack() with { Others = [RockNow(0, RockNowPlace)], Lands = [], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, NavmeshKey, Failure: null), Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>The cleaned mod moved the navmesh onto its raised ground; switched off, the navmesh stays there and keeps that ground.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TheGroundUnderTheCleanedModsNavmeshIsKeptWhileTheNavmeshesAreSwitchedOff(bool navmeshesOn, bool kept)
    {
        var onTheRaisedGround = MovedNavmesh() with { Current = Square(TestTargets.TargetName, NavmeshNow with { Z = Raised }) };
        var scene = new Scene { Navmeshes = [onTheRaisedGround], Settings = AllOn with { Navmeshes = navmeshesOn } };

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(20, 20)]);
    }

    /// <summary>
    /// Many rocks going back across the cell, some next to later mods' crates, with the navmesh, the house and the water: the results
    /// are the same with one worker or eight, and whatever the order of the records and objects read.
    /// </summary>
    [Fact]
    public void TheResultsDoNotDependOnTheNumberOfWorkersOrTheOrderOfTheRecordsRead()
    {
        var scene = ManyRocks();
        var random = new Random(7);
        var shuffled = scene with { Rocks = [.. scene.Rocks.OrderBy(_ => random.Next())], Others = [.. scene.Others.OrderBy(_ => random.Next())] };

        var results = new[] { scene with { Workers = 1 }, scene with { Workers = 8 }, shuffled with { Workers = 1 }, shuffled with { Workers = 8 } }.Select(run => Describe(Plan(run))).ToList();

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Contains(Plan(scene).Objects, judged => judged.Refused is not null);
    }

    /// <summary>A scene that takes several passes and holds a navmesh, a house, a tree, a rock and a water cell.</summary>
    private static Scene SceneOfSeveralPasses() => RockInTheNavmeshTakenBack() with
    {
        Others = [.. RockInTheNavmeshTakenBack().Others, TestShapes.Placed(BaseGameMod, 11, Tree.Base, new Vector3(3000, 1000, 0)), LaterObject(12, Block.Base, new Vector3(CellCentre + 300, CellCentre, 0))],
        Cells = [ChangedWater()],
    };

    [Fact]
    public void TheOrdersDoNotDependOnTheNumberOfWorkers()
    {
        var scene = SceneOfSeveralPasses();

        var results = new[] { 1, 2, 8 }.Select(workers => Describe(Plan(scene with { Workers = workers }))).ToList();

        Assert.All(results, result => Assert.Equal(results[0], result));
    }

    /// <summary>
    /// The cleaned mod moved a base game door and relinked it to another triangle of a base game navmesh it did not change. The door
    /// goes back with its earlier link when the consistency check finds no fault with its restore written; with a fault it keeps the
    /// cleaned mod's place and link, naming the navmesh at fault; a link a later plugin changed again keeps it too.
    /// </summary>
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, PlacedGuard.DoorLinkDoesNotMatch)]
    [InlineData(false, true, PlacedGuard.DoorLinkChangedLater)]
    public void ADoorTheCleanedModMovedGoesBackOnlyWithItsEarlierNavmeshLinkWhereThatLinkFits(bool fault, bool linkChangedLater, PlacedGuard? refused)
    {
        PartOutcome outcome = linkChangedLater ? new PartOutcome.LeftToLaterPlugin(LaterMod.ToPluginName()) : new PartOutcome.Undo();
        var door = MovedDoor(outcome, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0), new NavDoorLinkFact(UnchangedNavmesh, Triangle: 1));
        var scene = new Scene
        {
            Rocks = [door],
            Others = [RockNow(DoorIndex, DoorNow)],
            Lands = [],
            FindFault = trial => fault && trial.RestoredParts.Any(restored => restored.Record == door.Key)
                ? new NavmeshFault(UnchangedNavmesh, NavmeshRule.Doors, NavmeshFaultKind.DoorLinkWithoutDoorTriangle, 0, door.Key)
                : null,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(refused, judged.Refused?.Guard);
        Assert.Equal(refused == PlacedGuard.DoorLinkDoesNotMatch ? UnchangedNavmesh : null, judged.Refused?.By);
        Assert.Equal(linkChangedLater ? PlacedParts.Position : PlacedParts.Position | PlacedParts.NavDoorLink, judged.Change.Earlier.Undo);
    }

    /// <summary>
    /// The cleaned mod moved a base game door and gave it a door triangle of the navmesh it moved; with the navmeshes switched off that
    /// navmesh stays as the cleaned mod made it and names the door at another triangle than the door's earlier link, so the door stays.
    /// </summary>
    [Fact]
    public void ADoorWhoseEarlierLinkANavmeshThatStaysChangedDoesNotAnswerStays()
    {
        var door = MovedDoor(new PartOutcome.Undo(), new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0), new NavDoorLinkFact(NavmeshKey, Triangle: 1));
        var navmesh = MovedNavmesh() with { Current = Square(TestTargets.TargetName, NavmeshNow) with { DoorTriangles = [new NavmeshDoorTriangleFact(1, door.Key)] } };
        var scene = new Scene { Rocks = [door], Others = [RockNow(DoorIndex, DoorNow)], Navmeshes = [navmesh], Lands = [], Settings = AllOn with { Navmeshes = false } };

        Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, NavmeshKey, Failure: null), Assert.Single(Plan(scene).Objects).Refused);
    }

    /// <summary>
    /// The cleaned mod moved a base game door and the navmesh whose door triangle names it, and relinked the door to the moved triangle.
    /// The door and the navmesh go back together: the house standing in the navmesh's earlier place keeps the navmesh, and the door
    /// with it; a crate beside the door's old place keeps the door, and the navmesh with it. The door's restore writes its link, the
    /// navmesh group does not.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ADoorThatTakesItsLinkWithItAndTheNavmeshItNamesGoBackTogetherOrNotAtAll(bool houseStands, bool crateAtTheDoor)
    {
        var earlier = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var current = new NavDoorLinkFact(NavmeshKey, Triangle: 1);
        var door = MovedDoor(new PartOutcome.Undo(), earlier, current);
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(earlier.Triangle, door.Key)] },
            Current = Square(TestTargets.TargetName, NavmeshNow) with { DoorTriangles = [new NavmeshDoorTriangleFact(current.Triangle, door.Key)] },
            Doors = [new DoorLinkChange(door.Key, new PartOutcome.Undo(), earlier, current, InDeletedCell: false)],
        };
        List<OtherObject> others =
        [
            RockNow(DoorIndex, DoorNow),
            .. houseStands ? [LaterObject(10, House.Base, HousePlace with { Z = -10 })] : Array.Empty<OtherObject>(),
            .. CrateBesideTheRock(DoorOldPlace, crateAtTheDoor),
        ];

        var result = Plan(new Scene { Rocks = [door], Others = others, Navmeshes = [navmesh], Lands = [] });

        var judged = Assert.Single(result.Objects);
        var group = Assert.Single(result.NavmeshGroups);
        var together = !houseStands && !crateAtTheDoor;
        Assert.Equal((together, together), (judged.IsRestored, group.Write is not null));
        if (houseStands) Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, NavmeshKey, Failure: null), judged.Refused);
        if (crateAtTheDoor) Assert.Equal(new NavmeshRefusal(NavmeshGuard.Door, door.Key, Fault: null), group.Refused);
        Assert.False(group.Write is { } write && write.DoorLinksCopied.Concat(write.DoorLinksCleared).Contains(door.Key));
    }

    /// <summary>
    /// The cleaned mod edited the navmesh and deleted the base game door its earlier door triangle names. The consistency check over the
    /// group's orders sees the door brought back by its own restore, so the group goes back with it.
    /// </summary>
    [Fact]
    public void TheConsistencyCheckOfANavmeshGroupSeesTheRestoreOfADoorTheCleanedModDeleted()
    {
        var link = new NavDoorLinkFact(NavmeshKey, Triangle: 0);
        var deleted = Rock(DoorIndex, DoorOldPlace, DoorNow) with
        {
            Earlier = Rock(DoorIndex, DoorOldPlace, DoorNow).Earlier with { Undo = PlacedParts.Presence },
            DoorLink = new DoorLinkChange(DoorKey, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false),
        };
        var navmesh = MovedNavmesh() with
        {
            Earlier = Square(BaseGame, HousePlace with { Z = 0 }) with { DoorTriangles = [new NavmeshDoorTriangleFact(link.Triangle, DoorKey)] },
            Doors = [new DoorLinkChange(DoorKey, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false)],
        };
        var scene = new Scene
        {
            Rocks = [deleted],
            Navmeshes = [navmesh],
            Lands = [],
            Presence = Disabled(DoorKey),
            FindFault = trial => trial.RestoredParts.Any(restored => restored.Record == DoorKey && restored.Parts.HasFlag(PlacedParts.Presence))
                ? null
                : new NavmeshFault(NavmeshKey, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, 0, DoorKey),
        };

        var result = Plan(scene);

        Assert.True(Assert.Single(result.Objects).IsRestored);
        Assert.NotNull(Assert.Single(result.NavmeshGroups).Write);
    }

    /// <summary>
    /// The cleaned mod deleted two base game doors that a base game navmesh it did not change names at its door triangles. The navmesh
    /// is consistent only with both doors back, so both go back; a crate beside the first door's old place keeps it, and the second
    /// with it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoDeletedDoorsThatANavmeshThatStaysNamesGoBackTogetherOrNotAtAll(bool crateAtTheFirstDoor)
    {
        var secondOldPlace = DoorOldPlace + new Vector3(0, 1000, 0);
        var first = DeletedDoor(DoorIndex, DoorOldPlace, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 0));
        var second = DeletedDoor(DoorIndex + 1, secondOldPlace, new NavDoorLinkFact(UnchangedNavmesh, Triangle: 1));
        var scene = new Scene
        {
            Rocks = [first, second],
            Others = CrateBesideTheRock(DoorOldPlace, crateAtTheFirstDoor),
            Lands = [],
            Presence = Disabled(first.Key, second.Key),
            FindFault = trial => new[] { first.Key, second.Key }.All(door => trial.RestoredParts.Any(restored => restored.Record == door && restored.Parts.HasFlag(PlacedParts.Presence)))
                ? null
                : new NavmeshFault(UnchangedNavmesh, NavmeshRule.Doors, NavmeshFaultKind.DoorMissing, 0, first.Key),
        };

        var objects = Plan(scene).Objects;

        Assert.Equal([!crateAtTheFirstDoor, !crateAtTheFirstDoor], objects.Select(judged => judged.IsRestored));
        if (!crateAtTheFirstDoor) return;
        Assert.Equal(PlacedGuard.TooCloseToWhatStays, objects[0].Refused?.Guard);
        Assert.Equal(new PlacedRefusal(PlacedGuard.DoorLinkDoesNotMatch, UnchangedNavmesh, Failure: null), objects[1].Refused);
    }

    /// <summary>A base game door the cleaned mod deleted, whose presence and navmesh link go back.</summary>
    private static RecordChangedByCleanedMod DeletedDoor(int index, Vector3 place, NavDoorLinkFact link)
    {
        var door = Rock(index, place, place);
        return door with
        {
            Earlier = door.Earlier with { Undo = PlacedParts.Presence },
            DoorLink = new DoorLinkChange(door.Key, new PartOutcome.Undo(), link, Current: null, InDeletedCell: false),
        };
    }

    /// <summary>
    /// A later mod's invisible wall, or a primitive of it on a collision layer that stops characters, stands in the navmesh's earlier
    /// place, so the navmesh stays; a trigger primitive lets characters through, so it goes back.
    /// </summary>
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, (uint)CollisionLayer.CollisionBox, true)]
    [InlineData(true, (uint)CollisionLayer.Trigger, false)]
    public void AnInvisibleObjectThatStopsCharactersInTheNavmeshsEarlierPlaceKeepsTheNavmesh(bool primitive, uint? layer, bool refused)
    {
        var scene = BlockerInTheNavmeshsEarlierPlace(primitive, layer);

        Assert.Equal(refused ? new NavmeshRefusal(NavmeshGuard.Object, scene.Others[0].Key, Fault: null) : null, Assert.Single(Plan(scene).NavmeshGroups).Refused);
    }

    /// <summary>
    /// The collision layer records cannot be parsed: the step stops with that failure once the object guard first asks about the
    /// primitive's layer, and judges nothing, so the run goes on and the patch is written without anything of this step.
    /// </summary>
    [Fact]
    public void AMalformedCollisionLayerRecordTheObjectGuardMeetsStopsTheStepWithItsFailureAndJudgesNothing()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mod = TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE));
            var dataPath = TestShapes.AddStatics(mod, ShapeDataFolder, Statics);
            using var plugin = BrokenCollisionLayerData.WriteAndOpen(mod, folder);
            var scene = BlockerInTheNavmeshsEarlierPlace(primitive: true, (uint)CollisionLayer.CollisionBox) with
            {
                Shapes = TestVisibility.Over(TestShapes.Catalog(plugin.ToImmutableLinkCache(), dataPath, ShapeMod)),
            };

            var result = Plan(scene);

            Assert.Equal(StepOutcome.StoppedByUnexpectedError, result.Outcome);
            Assert.Empty(result.NavmeshGroups);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(RestoreWhereRemovedParts.Step, failure.Part);
            Assert.StartsWith(nameof(MalformedRecordException), failure.Failure, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A later mod's invisible wall, or a primitive of it on the given collision layer, stands in the navmesh's earlier place.</summary>
    private static Scene BlockerInTheNavmeshsEarlierPlace(bool primitive, uint? layer)
    {
        var blocker = primitive
            ? LaterObject(10, MarkerBase.Base, HousePlace with { Z = 0 }) with { IsPrimitive = true }
            : LaterObject(10, CollisionWall.Base, HousePlace with { Z = 0 });
        var facts = TestPlacedRecords.PlainReference with { IsPrimitive = true, PrimitiveBounds = new Vector3(2 * RockHalfSize), CollisionLayer = layer };
        return new Scene
        {
            Others = [blocker],
            Navmeshes = [MovedNavmesh()],
            Lands = [],
            Primitives = primitive ? new Dictionary<RecordKey, ReferenceFacts> { [blocker.Key] = facts } : new(),
        };
    }

    /// <summary>
    /// A later mod's crate stands next to the rock's old place, enabled with one of the cleaned mod's objects the run removes, or on its
    /// own: only the crate that stands in the final world keeps the rock out.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnObjectEnabledWithAnObjectTheRunRemovesKeepsNoRockOut(bool enabledWithARemovedObject, bool refused)
    {
        var oldPlace = new Vector3(1500, 1500, RockHalfSize);
        var crate = LaterObject(10, Block.Base, oldPlace + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, oldPlace, RockNowPlace)],
            Others = [crate, RockNow(0, RockNowPlace)],
            Lands = [],
            Presence = enabledWithARemovedObject ? new Dictionary<RecordKey, PlacedPresence> { [crate.Key] = new(Removed: false, TestTargets.Key(0), OppositeOfParent: false) } : new(),
        };

        Assert.Equal(refused ? PlacedGuard.TooCloseToWhatStays : null, Assert.Single(Plan(scene).Objects).Refused?.Guard);
    }

    /// <summary>
    /// Rock C is refused next to a later mod's crate, so it stays where it stands now, next to rock D's old place, and refuses D in the
    /// pass after. Rock E far away goes back.
    /// </summary>
    [Fact]
    public void ARockRefusedInOnePassRefusesTheRockWhoseOldPlaceItStandsBesideInTheNext()
    {
        var cOldPlace = new Vector3(1500, 1500, RockHalfSize);
        var cNow = new Vector3(1500, 2500, Raised + RockHalfSize);
        var dOldPlace = cNow + new Vector3(RockHalfSize + TooCloseDistance - 5f, 0, 0);
        var eOldPlace = new Vector3(3000, 3000, RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, cOldPlace, cNow), Rock(1, dOldPlace, new Vector3(2500, 500, Raised)), Rock(2, eOldPlace, new Vector3(3200, 3000, Raised))],
            Others = [.. CrateBesideTheRock(cOldPlace, present: true), RockNow(0, cNow), RockNow(1, new Vector3(2500, 500, Raised)), RockNow(2, new Vector3(3200, 3000, Raised))],
            Lands = [],
        };

        var result = Plan(scene);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, scene.Rocks[0].Key, Failure: null), result.Objects[1].Refused);
        Assert.Null(result.Objects[2].Refused);
    }

    /// <summary>A plugin deletes the cell a rock or a LAND record would be written into, so neither goes back.</summary>
    [Fact]
    public void NothingGoesBackIntoACellAPluginDeletes()
    {
        var rock = Rock(0, new Vector3(1500, 1500, RockHalfSize), RockNowPlace) with { InDeletedCell = true };
        var scene = new Scene { Rocks = [rock], Others = [RockNow(0, RockNowPlace)], Lands = [RaisedLand() with { CellDeletedInAVersion = true }] };

        var result = Plan(scene);

        Assert.Equal((NeverRestoredReason.InDeletedCell, false), (Assert.Single(result.Objects).Never, result.Objects[0].IsRestored));
        Assert.Equal((NeverRestoredReason.InDeletedCell, false), (Assert.Single(result.Lands).Never, result.Lands[0].IsRestored));
    }

    /// <summary>
    /// The placed guards answer the same over the objects within their reach as over every object: 144 rocks going back, every fifth
    /// next to a later mod's crate and every seventh carrying a later mod's block where it stands now, which refuses nothing, among
    /// later mods' blocks far away. Only the objects within reach are indexed.
    /// </summary>
    [Fact]
    public void ThePlacedGuardsAnswerTheSameOverTheObjectsWithinTheirReachAsOverEveryObject()
    {
        const float Everywhere = 20000f;
        var scene = ManyRocks();
        List<OtherObject> carried = [.. scene.Rocks.Where((_, index) => index % 7 == 0).Select((rock, index) => LaterObject(3000 + index, Block.Base, rock.WhereItIsNow!.Value.Position + new Vector3(0, 0, 2 * RockHalfSize)))];
        List<OtherObject> farAway = [.. Enumerable.Range(0, 50).Select(index => LaterObject(4000 + index, Block.Base, new Vector3(8000 + 50 * index, 6000, RockHalfSize)))];
        var everything = scene with { Others = [.. scene.Others, .. carried, .. farAway] };

        var (withinReach, indexedWithinReach) = CheckPlacedRecords(everything, AllOn.TooClose, Support.TouchGap);
        var (overEverything, indexedEverywhere) = CheckPlacedRecords(everything, AllOn.TooClose with { DistanceInUnits = Everywhere }, Everywhere);

        Assert.Equal(overEverything, withinReach);
        Assert.Contains(withinReach, refusal => refusal?.Guard == PlacedGuard.TooCloseToWhatStays);
        Assert.True(indexedWithinReach <= indexedEverywhere - farAway.Count);
    }

    /// <summary>The first pass of the placed guards over every rock of the scene going back, and how many objects their caches index.</summary>
    /// <param name="reachSettings">The too-close settings that set how far the guards look.</param>
    /// <param name="touchGap">The touch gap that sets how far the guards look.</param>
    private static (List<PlacedRefusal?> Found, int Indexed) CheckPlacedRecords(Scene scene, TooCloseSettings reachSettings, float touchGap)
    {
        var removedTargets = RemovedTargets();
        ImmutableArray<TargetObject> targets = [.. removedTargets];
        var collected = WorldOf(targets, scene.Others) with { ChangedRecords = [.. scene.Rocks] };
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        List<RecordChangedByCleanedMod> cleared = [.. scene.Rocks.OrderBy(rock => rock.Key, RecordKeyOrder.Comparer)];
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), threads: 1);
        var reach = new GuardReach(Shapes, rules.TooCloseToWhatStays, reachSettings, touchGap);
        var world = AfterRemovalWorld.Of(collected, final, OtherModsObjectsResult.SwitchedOff, TestTargets.TargetName, cleared, reach);
        var blocking = new ObjectsThatBlockActors(Shapes, new Dictionary<RecordKey, ReferenceFacts>());
        var presence = FinalPresence.Of(collected, removedTargets.Select(target => target.Key).ToHashSet(), cleared);
        var inputs = new PlacedGuardInputs(
            (checkedWorld, excluded) => rules.TooClose(checkedWorld, rules.WorldCaches.Of(checkedWorld), excluded, AllOn.TooClose),
            blocking,
            new NavmeshIndex(_ => null),
            CloneGuards.Of(world, blocking, [], [], new Execution(1)));
        var guards = new PlacedGuards(world, inputs, new DoorLinkGuard(cleared, [], _ => null), new Execution(1));
        var plans = new OtherKindsAsPlanned([], new HashSet<RecordKey>());
        var goingBack = Enumerable.Range(0, cleared.Count).ToHashSet();
        var (withClone, pass) = (new HashSet<int>(), new PresenceInPass(presence, goingBack.Contains));
        var followUp = new FollowUpOfGoingBack(rules.FollowUpOver(collected, TestPlacedGuardRules.DefaultFollowUp), final, OtherModsObjectsResult.SwitchedOff.TakenAway(), cleared, collected);
        var followUpChecks = followUp.Check(goingBack, withClone, pass, land: null);
        var standing = StandingTooCloseChecks.Of(collected, OtherModsObjectsResult.SwitchedOff, world, cleared, [], new HashSet<int>(), TestTargetGroups.None.TooClose, TestTargetGroups.None.CleanedModsObjects.TooClose, presence, rules.TooCloseToWhatStays, new Execution(1))
            .Check(goingBack, withClone, pass, followUpChecks);
        var found = guards.CheckGoingBack(goingBack, withClone, plans, pass, followUpChecks, standing).Found;
        return ([.. found], world.World.OtherModAndSupportOnlyObjects.Count());
    }

    private const int DoorIndex = 1;

    /// <summary>A base game navmesh the cleaned mod did not change.</summary>
    private static readonly RecordKey UnchangedNavmesh = new(BaseGame, 0x703);

    private static readonly Vector3 DoorOldPlace = new(3000, 500, RockHalfSize);
    private static readonly Vector3 DoorNow = DoorOldPlace + new Vector3(0, 300, 0);
    private static readonly RecordKey DoorKey = new FormKey(BaseGameMod, 0x900 + DoorIndex).ToRecordKey();

    /// <summary>A base game door the cleaned mod moved, whose navmesh link it changed as <paramref name="outcome"/> says.</summary>
    private static RecordChangedByCleanedMod MovedDoor(PartOutcome outcome, NavDoorLinkFact earlier, NavDoorLinkFact current) =>
        Rock(DoorIndex, DoorOldPlace, DoorNow) with { DoorLink = new DoorLinkChange(DoorKey, outcome, earlier, current, InDeletedCell: false) };

    private static Scene RockInTheNavmeshTakenBack() => WithAHouseKeepingItsGround(new()
    {
        Rocks = [Rock(0, NavmeshNow + new Vector3(0, 0, RockHalfSize / 2), RockNowPlace)],
        Others = [RockNow(0, RockNowPlace)],
        Navmeshes = [MovedNavmesh()],
    });

    /// <summary>
    /// The scene with a base game house standing on the raised ground at <see cref="HousePlace"/>, numbered after its other objects, and
    /// KeepSupportingObject: the ground under the house stays once the plan would lower it.
    /// </summary>
    private static Scene WithAHouseKeepingItsGround(Scene scene) => scene with
    {
        Others = [.. scene.Others, TestShapes.Placed(BaseGameMod, 10, House.Base, HousePlace)],
        Ground = RaisedGround(),
        Support = Support with { Handling = OtherModsObjectsHandling.KeepSupportingObject },
        Trust = BaseGameTrusted,
    };

    private static string Describe(RestoreWhereRemovedResult result) => string.Join(
        Environment.NewLine,
        result.Objects.Select(judged => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored}")
            .Concat(result.Cells.Select(cell => $"{cell.Change.Cell} {string.Join(",", cell.Undone.Select(part => part.Name))} {cell.WaterKeptBy} {cell.IsRestored}"))
            .Concat(result.Lands.Select(land =>
                $"{land.Change.Cell} {string.Join(",", land.Heights.Points)} {string.Join(";", land.Kept.Select(kept => $"{kept.Reason}:{string.Join(",", kept.Points.Points)}"))} {string.Join(",", land.FinalHeights ?? [])}"))
            .Concat(result.NavmeshGroups.Select(group => $"{string.Join(",", group.Members)} {group.Refused} {NavmeshWriteDescription.Of(group.Write)}"))
            .Append($"{result.Passes}"));

    private static int PointAt(int x, int y) => y * Side + x;

    /// <summary>The cleaned mod's removed objects: at every quarter centre of cell (0, 0) and its neighbour to the east, and at the centre of cell (0, 0).</summary>
    private static List<TargetObject> RemovedTargets()
    {
        List<Vector3> places = [new(CellCentre, CellCentre, Raised)];
        for (var cellX = 0; cellX <= 1; cellX++)
        {
            foreach (var (quarterX, quarterY) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            {
                places.Add(new Vector3(cellX * ExteriorGrid.CellSize + (2 * quarterX + 1) * QuarterCentre, (2 * quarterY + 1) * QuarterCentre, Raised));
            }
        }
        return [.. places.Select((place, index) => TestTargets.Create(index, TestTargets.At(place), Block.Base, TestTargets.Space))];
    }

    /// <summary>A base game object the cleaned mod moved, which no round removes, so it stands where it is.</summary>
    private static TargetObject StandingTarget(int index, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space) with { Origin = TargetOrigin.Moved };

    /// <summary>The key of the first object of <see cref="Scene.Standing"/>.</summary>
    private static RecordKey FirstStandingTargetKey() => TestTargets.Key(RemovedTargets().Count);

    /// <summary>The cleaned mod raised every point of the base game's flat ground of cell (0, 0).</summary>
    private static LandChangedByCleanedMod RaisedLand()
    {
        var all = PointSet.Of(Enumerable.Range(0, TerrainGrid.VertexCount));
        return new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 0, 0),
            new RecordKey(BaseGame, 0x600),
            all,
            PointSet.Of([]),
            default,
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            new LandHeights([.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(Raised, TerrainGrid.VertexCount)]));
    }

    /// <summary>The LAND record of cell (0, 0) with the base game's flat ground in both versions, for scenes whose terrain goes nowhere.</summary>
    private static LandChangedByCleanedMod FlatLand() =>
        RaisedLand() with { Heights = new LandHeights([.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)], [.. Enumerable.Repeat(0f, TerrainGrid.VertexCount)]) };

    /// <summary>Each plugin's version of a LAND record painted with a texture of its own.</summary>
    private static TerrainLayers? PaintedPerPlugin(PluginName plugin) => TestTerrainView.PaintedWith(new RecordKey(plugin, 0x300));

    /// <summary>Cell (0, 0), whose music and water level the cleaned mod changed: it lowered the water from 50 to −100.</summary>
    private static CellChangedByCleanedMod ChangedWater() => new(
        WaterCell,
        TestTargets.Space,
        (0, 0),
        [new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.Undo()), new PartChange(new CellPartName(CellPart.WaterLevel, null), new PartOutcome.Undo())],
        LinkedFromCleanedMod: false,
        TestTargets.TargetName,
        new CellWaterCopy(BaseGame, HasWater: true, EarlierWater),
        new CellWaterCopy(TestTargets.TargetName, HasWater: true, CleanedModsWater),
        DefaultWaterLevel: null);

    /// <summary><see cref="ChangedWater"/> with the earlier level from a plugin the changed-terrain step does not accept.</summary>
    private static CellChangedByCleanedMod ChangedUntrustedWater() =>
        ChangedWater() with { EarlierWater = new CellWaterCopy(LaterMod.ToPluginName(), HasWater: true, EarlierWater) };

    /// <summary>
    /// The cell, whose music and water level the cleaned mod changed: before it the cell used the worldspace's default water height 0,
    /// and the cleaned mod stores 0 as the cell's own height, so the level stays the same.
    /// </summary>
    private static CellChangedByCleanedMod WaterSetToTheDefault(RecordKey cell) => ChangedWater() with
    {
        Cell = cell,
        EarlierWater = new CellWaterCopy(BaseGame, HasWater: true, OwnLevel: null),
        CurrentWater = new CellWaterCopy(TestTargets.TargetName, HasWater: true, 0f),
        DefaultWaterLevel = 0f,
    };

    /// <summary>
    /// The navmesh the cleaned mod moved, held in the water cell: its version at <paramref name="at"/>, and a later plugin's version
    /// at <paramref name="laterWinnerAt"/> that wins when given.
    /// </summary>
    private static NavmeshChangedByCleanedMod NavmeshInTheWaterCell(Vector3 at, Vector3? laterWinnerAt) => MovedNavmesh() with
    {
        Cell = WaterCell,
        Current = Square(TestTargets.TargetName, at) with { Cell = WaterCell },
        LaterWinner = laterWinnerAt is { } later ? Square(LaterMod.ToPluginName(), later) with { Cell = WaterCell } : null,
    };

    /// <summary>
    /// 144 base game rocks the cleaned mod moved a little and raised, every fifth with a later mod's crate next to its old place, with
    /// the house, the moved navmesh and the water cell.
    /// </summary>
    private static Scene ManyRocks()
    {
        const int perSide = 12;
        const float spacing = 300f;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [LaterObject(1000, House.Base, HousePlace)];
        for (var index = 0; index < perSide * perSide; index++)
        {
            var oldPlace = new Vector3(spacing * (1 + index % perSide), spacing * (1 + index / perSide), RockHalfSize);
            var now = oldPlace + new Vector3(spacing / 2, 0, Raised);
            rocks.Add(Rock(index, oldPlace, now));
            others.Add(RockNow(index, now));
            if (index % 5 == 0) others.Add(LaterObject(2000 + index, Block.Base, oldPlace + new Vector3(0, RockHalfSize + TooCloseDistance - 5f, 0)));
        }
        return new Scene { Rocks = rocks, Others = others, Navmeshes = [MovedNavmesh()], Cells = [ChangedWater()] };
    }

    /// <summary>A base game navmesh, a flat square under the house, that the cleaned mod moved to <see cref="NavmeshNow"/>.</summary>
    private static NavmeshChangedByCleanedMod MovedNavmesh() => new(
        NavmeshKey,
        TestTargets.Space,
        NavmeshCell,
        TestTargets.TargetName,
        NavmeshCase.Edited,
        new PartOutcome.Undo(),
        new PartOutcome.Unchanged(),
        new PartOutcome.Unchanged(),
        Square(BaseGame, HousePlace with { Z = 0 }),
        Square(TestTargets.TargetName, NavmeshNow),
        ReplacedBy: [],
        Neighbours: [],
        Doors: [],
        NamedInPreferredPaths: false,
        WinningMapMod: null,
        CellDeletedInAVersion: false,
        LaterWinner: null,
        WinningMapEntry: null);

    private static NavmeshShape Square(PluginName plugin, Vector3 centre) => new(
        plugin,
        NavmeshCell,
        IsDeleted: false,
        [
            centre + new Vector3(-NavmeshHalfSize, -NavmeshHalfSize, 0), centre + new Vector3(NavmeshHalfSize, -NavmeshHalfSize, 0),
            centre + new Vector3(NavmeshHalfSize, NavmeshHalfSize, 0), centre + new Vector3(-NavmeshHalfSize, NavmeshHalfSize, 0),
        ],
        [new NavmeshTriangleFact(0, 1, 2, Preferred: false), new NavmeshTriangleFact(0, 2, 3, Preferred: false)],
        [],
        [],
        MapEntry: null);

    /// <summary>
    /// A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/>, whose position goes back;
    /// <paramref name="removedNow"/> when the cleaned mod also disabled it, so it stands nowhere now (see <see cref="Disabled"/>).
    /// </summary>
    private static RecordChangedByCleanedMod Rock(int index, Vector3 oldPlace, Vector3 now, bool removedNow = false) =>
        new(
            new FormKey(BaseGameMod, 0x900 + (uint)index).ToRecordKey(),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            Block.Base,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f), Block.Base) with { Undo = removedNow ? PlacedParts.Position | PlacedParts.Presence : PlacedParts.Position },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    /// <summary>The winning presence of records the game does not have.</summary>
    private static Dictionary<RecordKey, PlacedPresence> Disabled(params RecordKey[] records) =>
        records.ToDictionary(record => record, _ => new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false));

    /// <summary>The rock where the cleaned mod put it: its override is a change in the world without the cleaned mod.</summary>
    private static OtherObject RockNow(int index, Vector3 now) =>
        TestShapes.Placed(BaseGameMod, index, Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };

    private static OtherObject LaterObject(int index, BaseKey model, Vector3 position) =>
        TestShapes.Placed(LaterMod, index, model, position) with { ChangeWithoutCleanedMod = new ObjectChange(LaterMod.ToPluginName(), AcceptedFrom: null) };

    private static RestoreWhereRemovedResult Plan(Scene scene) => WhereRemovedRestorer.Plan(InputOf(scene));

    /// <summary>
    /// The targets and the other plugins' objects as the collector sorts them: a base game object no plugin changed shows its accepted
    /// values, so it is support only, numbered after the other-mod objects; every other one is an other-mod object.
    /// </summary>
    private static CollectedObjects WorldOf(ImmutableArray<TargetObject> targets, List<OtherObject> others)
    {
        List<OtherObject> causes = [.. others.Where(other => !IsSupportOnly(other))];
        List<OtherObject> supportOnly = [.. others.Where(IsSupportOnly).Select(other => other with { Change = null })];
        List<OtherObject> numbered = [.. causes.Concat(supportOnly).Select((other, index) => other with { Id = new OtherId(index) })];
        return TestScenes.CreateWorld(targets, [.. numbered.Take(causes.Count)], [.. numbered.Skip(causes.Count)]);
    }

    private static bool IsSupportOnly(OtherObject other) => other.WinningMod == BaseGame && other.ChangeWithoutCleanedMod is null;

    private static WhereRemovedRestorer.Input InputOf(Scene scene)
    {
        var removedTargets = RemovedTargets();
        var firstOwn = removedTargets.Count + scene.Standing.Count;
        ImmutableArray<TargetObject> targets =
        [
            .. removedTargets,
            .. scene.Standing.Select((position, index) => StandingTarget(removedTargets.Count + index, position) with { Base = scene.StandingModel }),
            .. scene.OwnStanding.Select((position, index) => TestTargets.Create(firstOwn + index, TestTargets.At(position), Block.Base, TestTargets.Space)),
        ];
        var collected = WorldOf(targets, scene.Others) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedCells = [.. scene.Cells],
            ChangedLands = [.. scene.Lands.Select(land => land with { EarlierPlugin = scene.EarlierLandPlugin })],
            ChangedNavmeshes = [.. scene.Navmeshes],
            WinningPresence = scene.Presence,
            WinningPrimitives = scene.Primitives,
            CleanedModsObjectsOverriddenLater = [.. scene.OverriddenLater],
        };
        var rules = TestPlacedGuardRules.Create(scene.Shapes, scene.Trust, Plugins, scene.Support, scene.Ground, scene.Workers);
        Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule>? tooClose = (scene.CheckedWorlds, scene.TooCloseStopsFor) switch
        {
            ({ } worlds, _) => (world, caches, excluded, settings) =>
            {
                worlds.Enqueue(world);
                return rules.TooClose(world, caches, excluded, settings);
            },
            (_, { } stopsFor) => (world, caches, excluded, settings) => new StoppingForRule(rules.TooClose(world, caches, excluded, settings), stopsFor),
            _ => null,
        };
        ImmutableArray<NavmeshWorldTriangle> winning =
        [
            .. scene.Navmeshes.SelectMany(navmesh => Enumerable.Range(0, navmesh.Winner.Triangles.Length)
                .Select(triangle => new NavmeshWorldTriangle(navmesh.Navmesh, navmesh.Winner.CornersOf(triangle)!.Value))),
            .. scene.Winning,
        ];
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removedTargets.Select(target => new ProposedRemoval(target.Id, new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        var caches = new TestRunCaches(
            scene.Shapes,
            terrain: scene.Ground,
            bases: scene.Bases,
            navmeshes: new NavmeshIndex(bucket => bucket.Grid is null && !winning.IsEmpty ? [.. winning] : null),
            shownTargets: TestScenes.Create(targets, [], scene.Shapes).IndexShownTargets);
        var accepted = new AcceptedWorld(
            TestTargets.PatchName,
            ImmutableDictionary<PluginName, VersionTrust>.Empty
                .Add(BaseGame, VersionTrust.Full)
                .Add(TestTargets.TargetName, VersionTrust.Full)
                .Add(LaterMod.ToPluginName(), scene.Trust.IsTrustedInFull(LaterMod.ToPluginName()) ? VersionTrust.Full : VersionTrust.None));
        var terrain = new TestTerrainView(scene.ViewLands ?? scene.Lands, scene.Cells, scene.EarlierLandPlugin, TestTargets.TargetName, scene.LandLayers, scene.OtherWater);
        var groups = TestTargetGroups.None with
        {
            Accepted = accepted,
            ChangedTerrainRule = scene.TerrainRule ?? ChangedTerrainRules.CreateJudging(accepted, Plugins, terrain, scene.Shapes),
            CleanedModsObjects = TestTargetGroups.None.CleanedModsObjects with { ChangedTerrainSettings = scene.CleanedModsTerrainCheck ?? TestTargetGroups.ChangedTerrainOff },
            TooClose = scene.StandingTooClose is { } standingTooClose
                ? TestTargetGroups.SettingsOf(Enumerable.Range(removedTargets.Count, scene.Standing.Count).Select(index => new TargetId(index)), standingTooClose)
                : TestTargetGroups.None.TooClose,
        };
        return new(
            new(collected, scene.OtherMods),
            final,
            TestClearedAreas.Of(collected, final, caches, scene.Settings),
            rules.Over(collected, rules.TooCloseRule(tooClose), navmeshCheck: new TestNavmeshCheck(scene.FindFault)),
            scene.StandingTerrainCheck is { } terrainCheck
                ? groups with
                {
                    ChangedTerrain = TestTargetGroups.SettingsOf(
                        Enumerable.Range(removedTargets.Count, scene.Standing.Count).Select(index => new TargetId(index)), terrainCheck),
                }
                : groups,
            new(
                caches,
                new TestLandPlugin(space => space, scene.ReadHeights),
                Plugins,
                TestTargets.TargetName),
            new Execution(scene.Workers),
            scene.Settings,
            () => TimeSpan.Zero);
    }

    /// <summary>What the cleaned mod changed besides its own removed objects, and the other plugins' objects that stand now, numbered in this order.</summary>
    private sealed record Scene
    {
        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The places of base game objects the cleaned mod moved that no round removes, numbered after the removed objects.</summary>
        public List<Vector3> Standing { get; init; } = [];

        /// <summary>The base of the objects of <see cref="Standing"/>.</summary>
        public BaseKey StandingModel { get; init; } = Block.Base;

        /// <summary>The changed-terrain step's settings the objects of <see cref="Standing"/> are judged with; none judges them when null.</summary>
        public ChangedTerrainSettings? StandingTerrainCheck { get; init; }

        /// <summary>The places of the cleaned mod's own objects that no round removes, numbered after <see cref="Standing"/>.</summary>
        public List<Vector3> OwnStanding { get; init; } = [];

        /// <summary>The changed-terrain step's settings of the cleaned mod's own objects, and so of a copy; the step is off for them when null.</summary>
        public ChangedTerrainSettings? CleanedModsTerrainCheck { get; init; }

        /// <summary>What the bases are, which tells whether a moved object may be copied.</summary>
        public IBaseFacts Bases { get; init; } = TestBaseFacts.Unresolved;

        public List<OtherObject> Others { get; init; } = [];

        /// <summary>The cleaned mod's objects later plugins hold, each also among <see cref="Others"/> under the same key.</summary>
        public List<TargetObject> OverriddenLater { get; init; } = [];

        /// <summary>What became of other mods' objects left without support; their ids are those the collector gives them.</summary>
        public OtherModsObjectsResult OtherMods { get; init; } = OtherModsObjectsResult.SwitchedOff;

        /// <summary>The ground as it is that objects rest on; none by default.</summary>
        public ITerrainHeights Ground { get; init; } = TestGround.NoTerrain();

        public OtherModsObjectsSettings Support { get; init; } = RestoreLoopTests.Support;

        public ModTrustTable Trust { get; init; } = RestoreLoopTests.Trust;

        public List<LandChangedByCleanedMod> Lands { get; init; } = [RaisedLand()];

        /// <summary>The LAND records whose versions the changed-terrain rule reads; those of <see cref="Lands"/> when null.</summary>
        public List<LandChangedByCleanedMod>? ViewLands { get; init; }

        /// <summary>The plugin of the LAND records' version below the cleaned mod's, as the changed-terrain rule reads it.</summary>
        public PluginName EarlierLandPlugin { get; init; } = BaseGame;

        /// <summary>The changed-terrain rule; the shared rule over the scene's terrain when null.</summary>
        public IChangedTerrainJudging? TerrainRule { get; init; }

        /// <summary>The texture layers of a LAND record's version by its plugin, as the changed-terrain rule reads them; none by default.</summary>
        public Func<PluginName, TerrainLayers?> LandLayers { get; init; } = _ => null;

        public List<CellChangedByCleanedMod> Cells { get; init; } = [];

        /// <summary>The water copies, in load order, of cells whose water the cleaned mod did not change, as the changed-terrain rule reads them.</summary>
        public Dictionary<ExteriorCell, CellWaterCopy[]> OtherWater { get; init; } = new();

        public List<NavmeshChangedByCleanedMod> Navmeshes { get; init; } = [];

        /// <summary>The winning presence of the placed records the game does not have or that follow an enable parent.</summary>
        public Dictionary<RecordKey, PlacedPresence> Presence { get; init; } = new();

        /// <summary>The triangles of winning navmeshes the cleaned mod did not change.</summary>
        public List<NavmeshWorldTriangle> Winning { get; init; } = [];

        /// <summary>The heights of a cell's LAND record below the cleaned mod and at the winner, for cells without a LAND fact.</summary>
        public Func<ExteriorCell, LandHeights> ReadHeights { get; init; } = _ => new LandHeights(null, null);

        /// <summary>What the winning version of each primitive says about the reference itself.</summary>
        public Dictionary<RecordKey, ReferenceFacts> Primitives { get; init; } = new();

        /// <summary>The consistency check's first fault over what the trial's writes alone would leave; none by default.</summary>
        public Func<NavmeshWriteTrial, NavmeshFault?> FindFault { get; init; } = _ => null;

        /// <summary>The too-close step's settings the objects of <see cref="Standing"/> are judged with; none judges them when null.</summary>
        public TooCloseSettings? StandingTooClose { get; init; }

        /// <summary>When set, every too-close check of the placed record with this key stops unexpectedly.</summary>
        public RecordKey? TooCloseStopsFor { get; init; }

        /// <summary>When set, collects the world of every too-close rule built, once per rule.</summary>
        public ConcurrentQueue<CollectedObjects>? CheckedWorlds { get; init; }

        public WhereRemovedRestoreSettings Settings { get; init; } = AllOn;
        public int Workers { get; init; } = 1;

        /// <summary>The base object shapes and collision layers the guards read.</summary>
        public IBaseObjectShapes Shapes { get; init; } = RestoreLoopTests.Shapes;
    }

    /// <summary>A too-close rule whose checks of the placed record with the given key stop unexpectedly.</summary>
    private sealed class StoppingForRule(IPlacementRule rule, RecordKey stopsFor) : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) => target.Key == stopsFor ? new Stopping() : rule.StartTries(target);

        private sealed class Stopping : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => new(Clash: null, StoppedCheck);

            public void Dispose()
            {
            }
        }
    }
}
