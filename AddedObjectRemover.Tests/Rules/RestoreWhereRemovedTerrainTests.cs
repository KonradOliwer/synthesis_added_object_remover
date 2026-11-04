using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Which heights, colours, quarter textures and rests of the LAND records the cleaned mod changed go back where it was removed:
/// the cleared quarters, cell edges shared by two records, the ground under objects that stay, the blend into ground that stays and
/// the heights the height map can store. Over in-memory target objects and LAND facts of a base game whose ground was flat at 0.
/// </summary>
public sealed class RestoreWhereRemovedTerrainTests
{
    private const int Side = TerrainGrid.VerticesPerSide;
    private const int Last = Side - 1;
    private const int Middle = Last / 2;
    private const float Raised = 80f;
    private const float QuarterCentre = ExteriorGrid.CellSize / 4;
    private const float HeightTolerance = 8f;
    private const float GridStep = TerrainGrid.VertexSpacing;
    private const float Step = TerrainGrid.UnitsPerHeightStep;

    /// <summary>A height range every change of the ground reaches.</summary>
    private static readonly (float Min, float Max) EveryHeight = (float.NegativeInfinity, float.PositiveInfinity);

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreTerrainShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreterrain\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(ShapeMod, "RestoreWhereRemovedTerrainData", Block));

    private static readonly PluginName BaseGame = new("Base.esm");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);

    private static readonly RecordKey Space = TestTargets.Space;
    private static readonly RecordKey ChildSpace = TestTargets.SpaceKey(0x101);

    private static readonly WhereRemovedRestoreSettings Settings = new(
        OtherModsObjects: false,
        Terrain: true,
        Cells: false,
        Navmeshes: false,
        new ClearedArea(SearchRadius: 1000f, MaxStandingSizePercent: 0.1, MaxStandingCountPercent: 0.1),
        TerrainEdgeBlendDistance: 0f,
        HeightTolerance,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: 10f, ZoneShape.BoundingBox, NpcHandling.Never));

    private static readonly INavmeshes NoNavmeshes = TestGround.Navmeshes(new Dictionary<RecordKey, List<CellNavmesh>>());

    /// <summary>
    /// Only the bottom left quarter's object was removed; no object of the cleaned mod stands near the other quarters. Every point of
    /// the cleared quarter goes back or is kept for a reason: its cell edges for the neighbouring cells, its lines to the other
    /// quarters for those quarters.
    /// </summary>
    [Fact]
    public void ALoneClearedQuarterGetsBackItsInnerPointsWhileItsCellEdgesAndTheOtherQuartersStay()
    {
        var scene = new Scene().Removed(QuarterCentreOf(0, 0, quarterX: 0, quarterY: 0)).Land(0, 0, RaisedEverywhere());

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(
            [AreaVerdict.Cleared, AreaVerdict.NoObjectsAround, AreaVerdict.NoObjectsAround, AreaVerdict.NoObjectsAround],
            land.Quarters.Select(quarter => quarter.Verdict));
        Assert.Equal(PointsWhere((x, y) => x is > 0 and < Middle && y is > 0 and < Middle), land.Heights);
        Assert.Equal((0f, Raised, Raised), (land.FinalHeights![PointAt(5, 5)], land.FinalHeights[PointAt(0, 5)], land.FinalHeights[PointAt(Middle, 5)]));
        Assert.Equal([TerrainKeptReason.OnEdgeOfLandThatStays, TerrainKeptReason.NextToQuarterThatStays], land.Kept.Select(kept => kept.Reason));
        Assert.Equal(PointsWhere((x, y) => (x == 0 || y == 0) && x < Middle && y < Middle).Points, KeptFor(land, TerrainKeptReason.OnEdgeOfLandThatStays).Points.Points);
        Assert.Equal(
            PointsWhere((x, y) => (x == Middle && y <= Middle) || (y == Middle && x <= Middle)).Points,
            KeptFor(land, TerrainKeptReason.NextToQuarterThatStays).Points.Points);
        Assert.Equal((Middle + 1) * (Middle + 1), land.Heights.Count + land.Kept.Sum(kept => kept.Points.Count));
        Assert.True(land.IsRestored);
    }

    /// <summary>The winning LAND record holds no height map, so none of the heights the cleaned mod changed can go back.</summary>
    [Fact]
    public void HeightsOfALandWithoutAHeightMapAreKeptForThat()
    {
        var withoutMap = RaisedEverywhere() with { Heights = new LandHeights(Heights((_, _) => 0f), Current: null) };

        var land = Assert.Single(Plan(ClearedAround(0, 0).Land(0, 0, withoutMap)).Lands);

        Assert.Equal(TerrainGrid.VertexCount, KeptFor(land, TerrainKeptReason.WithoutHeightMap).Points.Count);
        Assert.True(land.Heights.IsEmpty);
    }

    [Fact]
    public void ACellEdgeWhoseTwoRecordsHeldEqualEarlierHeightsGoesBackInBothToTheSameHeight()
    {
        var scene = ClearedAround(0, 0).Land(0, 0, RaisedEverywhere()).Land(1, 0, RaisedEverywhere());

        var lands = Plan(scene).Lands;

        Assert.Equal((0f, 0f), (lands[0].FinalHeights![PointAt(Last, 10)], lands[1].FinalHeights![PointAt(0, 10)]));
        Assert.True(lands[0].Heights.Contains(PointAt(Last, 10)) && lands[1].Heights.Contains(PointAt(0, 10)));
    }

    /// <summary>The second cell's base game ground was 40 units higher along its west edge.</summary>
    [Fact]
    public void ACellEdgeWhoseTwoRecordsHeldDifferentEarlierHeightsStaysInBoth()
    {
        var higherWest = RaisedEverywhere() with { Heights = new LandHeights(Heights((x, _) => x == 0 ? 40f : 0f), Heights((_, _) => Raised)) };
        var scene = ClearedAround(0, 0).Land(0, 0, RaisedEverywhere()).Land(1, 0, higherWest);

        var lands = Plan(scene).Lands;

        Assert.Equal((Raised, Raised), (lands[0].FinalHeights![PointAt(Last, 10)], lands[1].FinalHeights![PointAt(0, 10)]));
        Assert.True(KeptFor(lands[0], TerrainKeptReason.EarlierEdgeHeightsDiffer).Points.Contains(PointAt(Last, 10)));
        Assert.True(KeptFor(lands[1], TerrainKeptReason.EarlierEdgeHeightsDiffer).Points.Contains(PointAt(0, 10)));
    }

    /// <summary>The second cell's winning heights lie half a step above the first's steps, so no height on its edge is stored alike in both.</summary>
    [Fact]
    public void ACellEdgeWhoseTwoRecordsStoreHeightsOnDifferentStepsStaysInBoth()
    {
        var plan = PlanOf(0f, LandAt(0, 0, RaisedEverywhere()), LandAt(1, 0, RaisedEverywhere(by: Raised + Step / 2)));

        var lands = plan.Judged();

        Assert.True(KeptFor(lands[0], TerrainKeptReason.EdgeStoredOnDifferentSteps).Points.Contains(PointAt(Last, 10)));
        Assert.True(KeptFor(lands[1], TerrainKeptReason.EdgeStoredOnDifferentSteps).Points.Contains(PointAt(0, 10)));
        Assert.Equal((0f, 0f), (lands[0].FinalHeights![PointAt(10, 10)], lands[1].FinalHeights![PointAt(10, 10)] - Step / 2));
    }

    /// <summary>
    /// Four cells meet at the world origin, all cleared and raised by 80; one point next to the corner keeps the raise, so the corner,
    /// the first point of cell (0, 0), is blended to two thirds of it. Every height lies on the stored steps: the kept point and the
    /// edges the cells share read back exactly as planned.
    /// </summary>
    [Fact]
    public void PlannedHeightsLieOnTheStoredStepsSoKeptGroundAndSharedEdgesReadBackAsPlanned()
    {
        var plan = PlanOf(2 * GridStep, LandAt(-1, -1, RaisedEverywhere()), LandAt(-1, 0, RaisedEverywhere()), LandAt(0, -1, RaisedEverywhere()), LandAt(0, 0, RaisedEverywhere()))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [new LandGridPoint(new ExteriorCell(Space, 0, 0), PointAt(1, 0))]);

        var lands = plan.Judged();
        var read = lands.Select(land => TestLandHeights.Decode(land.FinalHeights!)).ToList();

        Assert.Equal(OnSteps(Raised * 2 / 3), lands[3].FinalHeights![PointAt(0, 0)]);
        Assert.All(lands.Zip(read), pair => Assert.Equal(pair.First.FinalHeights, pair.Second));
        Assert.Equal((Raised, Raised), (read[3][PointAt(1, 0)], read[2][PointAt(1, Last)]));
        Assert.Equal(Enumerable.Range(0, Side).Select(y => read[1][PointAt(Last, y)]), Enumerable.Range(0, Side).Select(y => read[3][PointAt(0, y)]));
        Assert.Equal(Enumerable.Range(0, Side).Select(x => read[2][PointAt(x, Last)]), Enumerable.Range(0, Side).Select(x => read[3][PointAt(x, 0)]));
    }

    /// <summary>Another plugin's object the cleaned mod moved stands in the middle of the cleared cell, on the raised ground or far above it.</summary>
    [Theory]
    [InlineData(Raised, true)]
    [InlineData(Raised + 1000f, false)]
    public void TheGroundUnderAnObjectThatStaysKeepsItsHeightWhereTheObjectReachesIt(float objectHeight, bool kept)
    {
        var centre = new Vector3(ExteriorGrid.CellSize / 2, ExteriorGrid.CellSize / 2, objectHeight);
        var scene = ClearedAround(0, 0).Standing(centre, TargetOrigin.Moved).Land(0, 0, RaisedEverywhere());

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept ? Raised : 0f, land.FinalHeights![PointAt(Middle, Middle)]);
        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectThatStays && points.Points.Contains(PointAt(Middle, Middle))));
    }

    /// <summary>The object in the middle of the cleared cell is enabled with a parent that ends enabled, or disabled and takes the object with it.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheGroundUnderAnObjectEnabledWithAParentIsKeptOnlyWhileTheParentIsEnabled(bool parentDisabled, bool kept)
    {
        var parent = new RecordKey(BaseGame, 0xA00);
        var standing = ClearedAround(0, 0).Standing(new Vector3(ExteriorGrid.CellSize / 2, ExteriorGrid.CellSize / 2, Raised), TargetOrigin.Moved).Land(0, 0, RaisedEverywhere());
        var scene = standing.WithPresence(standing.Targets[^1].Key, new PlacedPresence(Removed: false, parent, OppositeOfParent: false));
        if (parentDisabled) scene = scene.WithPresence(parent, new PlacedPresence(Removed: true, Parent: null, OppositeOfParent: false));

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal(kept, land.Kept.Any(points => points.Reason == TerrainKeptReason.UnderObjectThatStays && points.Points.Contains(PointAt(Middle, Middle))));
    }

    /// <summary>
    /// The cleaned mod raised the whole cell by 2000 units and still stands around three quarters; the bottom left quarter's heights
    /// would drop 2000 units within one grid step, beyond what the height map stores, unless blended over two steps.
    /// </summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(2 * GridStep, true)]
    public void ACellWhoseRestoredHeightsAreTooSteepToStoreKeepsThemAll(float blend, bool restored)
    {
        var scene = new Scene()
            .Removed(QuarterCentreOf(0, 0, 0, 0))
            .Standing(QuarterCentreOf(0, 0, 1, 0))
            .Standing(QuarterCentreOf(0, 0, 0, 1))
            .Standing(QuarterCentreOf(0, 0, 1, 1))
            .Land(0, 0, RaisedEverywhere(by: 2000f));

        var land = Assert.Single(Plan(scene, Settings with { TerrainEdgeBlendDistance = blend }).Lands);

        Assert.Equal(restored, land.FinalHeights is not null);
        Assert.Equal(!restored, land.Kept.Any(points => points.Reason == TerrainKeptReason.TooSteepToStore && points.Points.Contains(PointAt(5, 5))));
    }

    /// <summary>
    /// The whole cell is cleared and kept only along its edges, so the points of the middle row are 1, 2, 3 ... grid steps from the
    /// nearest point that stays; with a blend over n steps each moves 1 − k/(n+1) of the way back up, onto the nearest stored step.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheBlendMovesEachRestoredPointTowardsTheGroundThatStaysByItsGridDistance(int steps)
    {
        var scene = ClearedAround(0, 0).Land(0, 0, RaisedEverywhere());

        var land = Assert.Single(Plan(scene, Settings with { TerrainEdgeBlendDistance = steps * GridStep }).Lands);

        Assert.Equal(
            Enumerable.Range(0, steps + 2).Select(k => k == 0 ? Raised : OnSteps(Raised * MathF.Max(0f, 1f - (float)k / (steps + 1)))),
            Enumerable.Range(0, steps + 2).Select(k => land.FinalHeights![PointAt(k, Middle)]));
    }

    /// <summary>The cleaned mod raised a plateau in the middle of the cell by 200 units with a hard edge; the ground around it never changed.</summary>
    [Fact]
    public void ARestoredPlateauNextToGroundTheCleanedModNeverChangedGoesBackWholeWithoutARidge()
    {
        var plateau = PointsWhere((x, y) => x is >= 10 and <= 22 && y is >= 10 and <= 22);
        var raised = Unchanged() with { UndoPoints = plateau, Heights = new LandHeights(Heights((_, _) => 0f), Heights((x, y) => plateau.Contains(PointAt(x, y)) ? 200f : 0f)) };

        var land = Assert.Single(PlanOf(2 * GridStep, raised).Judged());

        Assert.Equal(plateau, land.Heights);
        Assert.All(land.FinalHeights!, height => Assert.Equal(0f, height));
    }

    /// <summary>
    /// The cleaned mod raised two mounds by 200 units, one grid line apart that it never touched; the west one goes back, the east one
    /// is kept. The west mound's edge point is one step from the untouched line and two from the kept mound: with a blend over two
    /// steps it goes back whole, as the untouched line stays, with no ridge between them.
    /// </summary>
    [Fact]
    public void ARestoredPointGoesBackWholeWhenGroundNobodyChangedLiesNearerThanGroundThatKeepsAChange()
    {
        var west = PointsWhere((x, y) => x is >= 5 and <= 9 && y is >= 10 and <= 20);
        var east = PointsWhere((x, y) => x is >= 11 and <= 15 && y is >= 10 and <= 20);
        var mounds = Unchanged() with
        {
            UndoPoints = PointSet.Of(west.Points.Concat(east.Points)),
            Heights = new LandHeights(Heights((_, _) => 0f), Heights((x, y) => west.Contains(PointAt(x, y)) || east.Contains(PointAt(x, y)) ? 200f : 0f)),
        };
        var cell = new ExteriorCell(Space, 0, 0);
        var plan = PlanOf(2 * GridStep, mounds).Protect(TerrainKeptReason.UnderObjectKeptAsChanged, east.Points.Select(point => new LandGridPoint(cell, point)));

        var heights = plan.PlannedHeightsOf(cell)!;

        Assert.Equal((0f, 0f, 200f), (heights[PointAt(9, 15)], heights[PointAt(10, 15)], heights[PointAt(11, 15)]));
    }

    /// <summary>A later plugin raised one point to 400; its neighbour, which the cleaned mod raised by 80, is one step away with a blend over one step.</summary>
    [Fact]
    public void ABlendedPointMovesByItsOwnChangeNotByTheChangeOfThePointThatKeepsIt()
    {
        var raisedAgain = PointAt(10, 10);
        var land = RaisedEverywhere() with
        {
            UndoPoints = PointSet.Of(RaisedEverywhere().UndoPoints.Points.Where(point => point != raisedAgain)),
            LeftToLater = new LandLeftToLater(PointSet.Of([raisedAgain]), PointSet.Of([]), default, Rest: false, [Later]),
            Heights = new LandHeights(Heights((_, _) => 0f), Heights((x, y) => x == 10 && y == 10 ? 5 * Raised : Raised)),
        };

        var judged = Assert.Single(PlanOf(GridStep, land).Judged());

        Assert.Equal((5 * Raised, Raised / 2, 0f), (judged.FinalHeights![raisedAgain], judged.FinalHeights[PointAt(11, 10)], judged.FinalHeights[PointAt(12, 10)]));
    }

    [Fact]
    public void ABlendOfZeroLeavesASharpEdge()
    {
        var land = Assert.Single(Plan(ClearedAround(0, 0).Land(0, 0, RaisedEverywhere())).Lands);

        Assert.Equal((Raised, 0f), (land.FinalHeights![PointAt(0, Middle)], land.FinalHeights[PointAt(1, Middle)]));
    }

    /// <summary>The cleaned mod recoloured a point in the bottom left and the top right quarter and painted both; it still stands around the top right one.</summary>
    [Fact]
    public void ColoursAndTexturesGoBackOnlyInClearedQuartersAndTheRestOnlyWhenEveryQuarterIsCleared()
    {
        var painted = Unchanged() with { UndoColours = PointSet.Of([PointAt(5, 5), PointAt(25, 25)]), UndoQuarters = QuarterSet.Of([0, 3]), UndoRest = true };
        var partly = new Scene()
            .Removed(QuarterCentreOf(0, 0, 0, 0))
            .Removed(QuarterCentreOf(0, 0, 1, 0))
            .Removed(QuarterCentreOf(0, 0, 0, 1))
            .Standing(QuarterCentreOf(0, 0, 1, 1))
            .Land(0, 0, painted);

        var land = Assert.Single(Plan(partly).Lands);
        var whole = Assert.Single(Plan(ClearedAround(0, 0).Land(0, 0, painted)).Lands);

        Assert.Equal((PointSet.Of([PointAt(5, 5)]), QuarterSet.Of([0]), false), (land.Colours, land.Textures, land.Rest));
        Assert.Equal((painted.UndoColours, painted.UndoQuarters, true), (whole.Colours, whole.Textures, whole.Rest));
        Assert.Null(whole.FinalHeights);
    }

    /// <summary>A later plugin raised one point again, which is therefore no point of the cleaned mod's to undo.</summary>
    [Fact]
    public void APointALaterPluginRaisedAgainKeepsItsHeight()
    {
        var raisedAgain = PointAt(10, 10);
        var land = RaisedEverywhere() with
        {
            UndoPoints = PointSet.Of(RaisedEverywhere().UndoPoints.Points.Where(point => point != raisedAgain)),
            LeftToLater = new LandLeftToLater(PointSet.Of([raisedAgain]), PointSet.Of([]), default, Rest: false, [Later]),
            Heights = new LandHeights(Heights((_, _) => 0f), Heights((x, y) => x == 10 && y == 10 ? 2 * Raised : Raised)),
        };

        var judged = Assert.Single(Plan(ClearedAround(0, 0).Land(0, 0, land)).Lands);

        Assert.Equal((2 * Raised, 0f), (judged.FinalHeights![raisedAgain], judged.FinalHeights[PointAt(20, 20)]));
        Assert.False(judged.Heights.Contains(raisedAgain));
    }

    [Fact]
    public void ALandALaterPluginWinsStays()
    {
        var land = Assert.Single(Plan(ClearedAround(0, 0).Land(0, 0, RaisedEverywhere() with { WinningMod = Later })).Lands);

        Assert.Equal((NeverRestoredReason.WinnerLoadsAfterPatch, false), (land.Never!.Value, land.IsRestored));
    }

    /// <summary>A child worldspace uses the worldspace's land; the cleaned mod still stands there over the bottom left quarter.</summary>
    [Fact]
    public void AQuarterOfLandSeveralSpacesUseGoesBackOnlyWhenNoneOfThemStillStandsThere()
    {
        var scene = ClearedAround(0, 0).Standing(QuarterCentreOf(0, 0, 0, 0), space: ChildSpace).Land(0, 0, RaisedEverywhere());

        var land = Assert.Single(Plan(scene).Lands);

        Assert.Equal([AreaVerdict.StillStanding, AreaVerdict.Cleared, AreaVerdict.Cleared, AreaVerdict.Cleared], land.Quarters.Select(quarter => quarter.Verdict));
        Assert.False(land.Heights.Contains(PointAt(5, 5)));
    }

    /// <summary>
    /// A placed record a guard keeps stands on a protected point, whose ground stays raised; the points one and two steps from it go
    /// back blended into it, and another point goes back whole. A box standing on the flat ground before the cleaned mod is judged
    /// against the ground the patch leaves: on the protected point and on the blend it would sink, where the ground goes back whole it
    /// stands as before. An area around a point reaches the squares on either side of it.
    /// </summary>
    [Theory]
    [InlineData(10, true)]
    [InlineData(12, true)]
    [InlineData(20, false)]
    public void GroundStaysChangedUnderAProtectedPointAndTheBlendAroundItButNotWhereItGoesBackWhole(int x, bool staysChanged)
    {
        var cell = new ExteriorCell(Space, 0, 0);
        var plan = PlanOf(2 * GridStep, RaisedEverywhere()).Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [new LandGridPoint(cell, PointAt(10, 10))]);

        var found = plan.GroundThatStaysChangedUnder(Space, AreaAround(x, 10), (0f, 2 * HeightTolerance));

        Assert.Equal((OnSteps(Raised * 2 / 3), OnSteps(Raised / 3)), (plan.PlannedHeightsOf(cell)![PointAt(11, 10)], plan.PlannedHeightsOf(cell)![PointAt(12, 10)]));
        Assert.Equal(staysChanged ? new GroundThatStaysChanged(cell, RaisedEverywhere().LandKey) : null, found);
    }

    /// <summary>
    /// A later plugin moved the ground of cell (1, 0), whose LAND record the cleaned mod does not override, from one height to another
    /// under a box from 1000 to 1100 units high: the change counts only where the heights between the two come within the tolerance of
    /// the box.
    /// </summary>
    [Theory]
    [InlineData(0f, 500f, false)]
    [InlineData(5000f, 3000f, false)]
    [InlineData(0f, 1000f - HeightTolerance - 0.5f, false)]
    [InlineData(0f, 1000f - HeightTolerance, true)]
    [InlineData(5000f, 1100f + HeightTolerance, true)]
    [InlineData(5000f, 1100f + HeightTolerance + 0.5f, false)]
    [InlineData(1050f, 0f, true)]
    [InlineData(0f, 5000f, true)]
    public void GroundStaysChangedUnderABoxOnlyWhereItsChangeReachesTheBoxHeights(float earlier, float final, bool staysChanged)
    {
        var changedLater = new ExteriorCell(Space, 1, 0);
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere())],
            cell => cell == changedLater ? new LandHeights(Heights((_, _) => earlier), Heights((_, _) => final)) : new LandHeights(null, null),
            HeightTolerance,
            blendDistance: 0f);

        var found = plan.GroundThatStaysChangedUnder(Space, AreaAround(Side + 10, 10), (1000f, 1100f));

        Assert.Equal(staysChanged ? new GroundThatStaysChanged(changedLater, Land: null) : null, found);
    }

    /// <summary>
    /// Cell (1, 0), whose LAND record the cleaned mod does not override, slopes 400 units across each grid step; a later plugin raised
    /// it by 50 units. A box 40 units high stands in the middle of one square, where the slope passes through it: each corner of the
    /// square lies far above or below the box, but the ground between them rises into it.
    /// </summary>
    [Fact]
    public void GroundStaysChangedUnderASmallBoxOnASteepSlopeBetweenGridPoints()
    {
        const float Slope = 400f;
        var changedLater = new ExteriorCell(Space, 1, 0);
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere())],
            cell => cell == changedLater ? new LandHeights(Heights((x, _) => (x - 10.5f) * Slope), Heights((x, _) => (x - 10.5f) * Slope + 50f)) : new LandHeights(null, null),
            HeightTolerance,
            blendDistance: 0f);
        var middle = new Vector2((TerrainGrid.SquaresPerSide + 10.5f) * GridStep, 10.5f * GridStep);

        var found = plan.GroundThatStaysChangedUnder(Space, (middle - new Vector2(5), middle + new Vector2(5)), (-20f, 20f));

        Assert.Equal(new GroundThatStaysChanged(changedLater, Land: null), found);
    }

    /// <summary>
    /// A later plugin raised only the north-east corner of one square of cell (1, 0), whose LAND record the cleaned mod does not
    /// override: by 30 units on flat ground, or by 20 on a slope rising 400 units to the north. A box 12.8 units across stands at the
    /// square's far corner, where the ground under it moves less than the tolerance, or under the raised corner.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void GroundStaysChangedUnderABoxOnlyWhereTheGroundUnderItsFootprintChanges(bool slope, bool underTheRaisedCorner, bool staysChanged)
    {
        const float SlopeRise = 400f;
        const int SquareX = 10;
        const int SquareY = 10;
        var changedLater = new ExteriorCell(Space, 1, 0);
        var raisedBy = slope ? 20f : 30f;
        float Earlier(int _, int y) => slope ? (y - SquareY) * SlopeRise : 0f;
        float Final(int x, int y) => Earlier(x, y) + (x == SquareX + 1 && y == SquareY + 1 ? raisedBy : 0f);
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere())],
            cell => cell == changedLater ? new LandHeights(Heights(Earlier), Heights(Final)) : new LandHeights(null, null),
            HeightTolerance,
            blendDistance: 0f);
        var inSquare = underTheRaisedCorner ? 0.9f : 0f;
        var southWest = new Vector2(TerrainGrid.SquaresPerSide + SquareX + inSquare, SquareY + inSquare) * GridStep;
        var groundUnder = slope && underTheRaisedCorner ? SlopeRise : 0f;

        var found = plan.GroundThatStaysChangedUnder(Space, (southWest, southWest + new Vector2(0.1f * GridStep)), (groundUnder - 20f, groundUnder + 30f));

        Assert.Equal(staysChanged ? new GroundThatStaysChanged(changedLater, Land: null) : null, found);
    }

    /// <summary>
    /// A later plugin raised one corner of a flat square of cell (1, 0), whose LAND record the cleaned mod does not override, by 30
    /// units; a small box stands at the square's centre. Split along the diagonal through the raised corner, the ground at the centre
    /// rises by half of it; split along the other diagonal, it barely moves. Which split the game uses is not known, so the box is
    /// refused on either: the north-east corner only the south-west to north-east split shows, the south-east corner only the other.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    public void GroundStaysChangedUnderABoxWhereEitherTriangleSplitShowsTheChange(int cornerX, int cornerY)
    {
        const int SquareX = 10;
        const int SquareY = 10;
        var changedLater = new ExteriorCell(Space, 1, 0);
        float Final(int x, int y) => x == SquareX + cornerX && y == SquareY + cornerY ? 30f : 0f;
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere())],
            cell => cell == changedLater ? new LandHeights(Heights((_, _) => 0f), Heights(Final)) : new LandHeights(null, null),
            HeightTolerance,
            blendDistance: 0f);
        var centre = new Vector2(TerrainGrid.SquaresPerSide + SquareX + 0.5f, SquareY + 0.5f) * GridStep;
        var halfWidth = new Vector2(0.05f * GridStep);

        var found = plan.GroundThatStaysChangedUnder(Space, (centre - halfWidth, centre + halfWidth), (0f, 2 * HeightTolerance));

        Assert.Equal(new GroundThatStaysChanged(changedLater, Land: null), found);
    }

    /// <summary>A later plugin raised the ground of cell (1, 0), whose LAND record the cleaned mod does not override, by 150 units.</summary>
    [Fact]
    public void GroundALaterPluginChangedInALandTheCleanedModDoesNotOverrideStaysChanged()
    {
        var raisedLater = new ExteriorCell(Space, 1, 0);
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere())],
            cell => cell == raisedLater ? new LandHeights(Heights((_, _) => 0f), Heights((_, _) => 150f)) : new LandHeights(null, null),
            HeightTolerance,
            blendDistance: 0f);

        Assert.Equal(new GroundThatStaysChanged(raisedLater, Land: null), plan.GroundThatStaysChangedUnder(Space, AreaAround(Side + 10, 10), EveryHeight));
        Assert.Null(plan.GroundThatStaysChangedUnder(Space, AreaAround(-10, 10), EveryHeight));
    }

    /// <summary>The cleaned mod only recoloured cell (1, 0), so its fact carries no heights; they are read when the plan needs them.</summary>
    [Fact]
    public void TheHeightsOfALandWhoseHeightsNoVersionChangedAreReadWhenNeeded()
    {
        var recoloured = new ExteriorCell(Space, 1, 0);
        var read = Heights((x, _) => 8f * x);
        var plan = new TerrainUndoPlan(
            [AllCleared(RaisedEverywhere()), AllCleared(LandAt(1, 0, Unchanged() with { UndoColours = PointSet.Of([PointAt(5, 5)]), Heights = null }))],
            cell => cell == recoloured ? new LandHeights(read, read) : throw new InvalidOperationException($"Cell {cell} carries its heights."),
            HeightTolerance,
            blendDistance: 0f);

        Assert.Equal(read, plan.CurrentHeightsOf(recoloured));
        Assert.Equal(read[PointAt(1, 10)], plan.Judged()[0].Beyond.East![10]);
    }

    /// <summary>
    /// A plan protecting more points than one already computed starts from that one's outcome; it ends as a plan given the same
    /// protection from the start. Cell (0, 0) is raised too steeply to store with only its bottom left quarter cleared; cell (2, 0)
    /// is blended around a point protected later.
    /// </summary>
    [Fact]
    public void APlanProtectingMorePointsEndsAsOneProtectingThemFromTheStart()
    {
        var standing = TestAreaShares.Quarter(AreaVerdict.StillStanding);
        var cleared = TestAreaShares.Quarter(AreaVerdict.Cleared);
        LandAreasJudged[] lands =
        [
            new(LandAt(0, 0, RaisedEverywhere(by: 4000f)), Never: null, [cleared, standing, standing, standing]),
            AllCleared(LandAt(2, 0, RaisedEverywhere())),
        ];
        var later = new LandGridPoint(new ExteriorCell(Space, 2, 0), PointAt(16, 16));
        var first = new TerrainUndoPlan(lands, _ => new LandHeights(null, null), HeightTolerance, 2 * GridStep);
        var tooSteep = first.PointsOfCellsTooSteepToStore().ToList();

        var protectedLater = first.Protect(TerrainKeptReason.TooSteepToStore, tooSteep).Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later]);
        var protectedFromTheStart = new TerrainUndoPlan(lands, _ => new LandHeights(null, null), HeightTolerance, 2 * GridStep)
            .Protect(TerrainKeptReason.TooSteepToStore, tooSteep)
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later]);

        Assert.NotEmpty(tooSteep);
        Assert.Equal(protectedFromTheStart.Judged().Select(Describe), protectedLater.Judged().Select(Describe));
        Assert.Equal(Raised, protectedLater.PlannedHeightsOf(later.Cell)![later.Point]);
    }

    /// <summary>
    /// As above, over cells (0, 0) and (1, 0), both cleared and raised: a point one step west of their shared edge is protected later,
    /// so the blend reaches across the edge into the neighbour, whose edge points must change with it.
    /// </summary>
    [Fact]
    public void APlanProtectingAPointNextToACellEdgeEndsAsOneProtectingItFromTheStart()
    {
        var later = new LandGridPoint(new ExteriorCell(Space, 0, 0), PointAt(Last - 1, Middle));
        var first = PlanOf(2 * GridStep, LandAt(0, 0, RaisedEverywhere()), LandAt(1, 0, RaisedEverywhere()));
        first.Judged();

        var protectedLater = first.Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later]).Judged();
        var protectedFromTheStart = PlanOf(2 * GridStep, LandAt(0, 0, RaisedEverywhere()), LandAt(1, 0, RaisedEverywhere()))
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later])
            .Judged();

        Assert.Equal(protectedFromTheStart.Select(Describe), protectedLater.Select(Describe));
        Assert.Equal(OnSteps(Raised / 3), protectedLater[1].FinalHeights![PointAt(1, Middle)]);
        Assert.Equal(protectedLater[0].FinalHeights![PointAt(Last, Middle)], protectedLater[1].FinalHeights![PointAt(0, Middle)]);
    }

    /// <summary>
    /// As above, over cells (0, 0) and (2, 0) raised by 80, where the cleaned mod raised one point of (2, 0) by 4000: protected later,
    /// it keeps (2, 0) from storing its heights, which a round of the plan protecting more points then takes back.
    /// </summary>
    [Fact]
    public void APlanWhoseLaterProtectionMakesACellTooSteepEndsAsOneProtectingItFromTheStart()
    {
        var spike = new LandGridPoint(new ExteriorCell(Space, 2, 0), PointAt(Middle, Middle));
        var spiked = RaisedEverywhere() with { Heights = new LandHeights(Heights((_, _) => 0f), Heights((x, y) => PointAt(x, y) == spike.Point ? 4000f : Raised)) };
        var first = PlanOf(2 * GridStep, LandAt(0, 0, RaisedEverywhere()), LandAt(2, 0, spiked));
        first.Judged();

        var protectedLater = first.Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [spike]);
        var protectedFromTheStart = PlanOf(2 * GridStep, LandAt(0, 0, RaisedEverywhere()), LandAt(2, 0, spiked)).Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [spike]);

        Assert.Equal(protectedFromTheStart.Judged().Select(Describe), protectedLater.Judged().Select(Describe));
        Assert.Contains(protectedLater.PointsOfCellsTooSteepToStore(), point => point.Cell == spike.Cell);
        Assert.Null(protectedLater.Judged()[1].FinalHeights);
    }

    /// <summary>
    /// As the first of these, but the later plan protects only the point of cell (2, 0), not those of the cell too steep to store, so
    /// it cannot start from the earlier outcome and computes afresh.
    /// </summary>
    [Fact]
    public void APlanThatDoesNotProtectAnEarlierTooSteepCellEndsAsOneComputedAfresh()
    {
        var standing = TestAreaShares.Quarter(AreaVerdict.StillStanding);
        var cleared = TestAreaShares.Quarter(AreaVerdict.Cleared);
        LandAreasJudged[] lands =
        [
            new(LandAt(0, 0, RaisedEverywhere(by: 4000f)), Never: null, [cleared, standing, standing, standing]),
            AllCleared(LandAt(2, 0, RaisedEverywhere())),
        ];
        var later = new LandGridPoint(new ExteriorCell(Space, 2, 0), PointAt(16, 16));
        var first = new TerrainUndoPlan(lands, _ => new LandHeights(null, null), HeightTolerance, 2 * GridStep);

        var protectedLater = first.Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later]);
        var protectedFromTheStart = new TerrainUndoPlan(lands, _ => new LandHeights(null, null), HeightTolerance, 2 * GridStep)
            .Protect(TerrainKeptReason.UnderObjectKeptAsChanged, [later]);

        Assert.NotEmpty(first.PointsOfCellsTooSteepToStore());
        Assert.Equal(protectedFromTheStart.Judged().Select(Describe), protectedLater.Judged().Select(Describe));
    }

    /// <summary>
    /// The cleaned mod recoloured a point on the edge between cells (0, 0) and (1, 0) in both records, and a point inside (0, 0). Its
    /// objects are removed around both cells, or still stand around the west quarters of (1, 0).
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AColourOnACellEdgeGoesBackInBothRecordsOrInNeither(bool eastCellStillStanding, bool edgeGoesBack)
    {
        var scene = eastCellStillStanding
            ? ClearedAround(0, 0) with { Removals = [0, 1, 2, 3, 5, 7] }
            : ClearedAround(0, 0);
        scene = scene
            .Land(0, 0, Unchanged() with { UndoColours = PointSet.Of([PointAt(5, 5), PointAt(Last, 10)]) })
            .Land(1, 0, Unchanged() with { UndoColours = PointSet.Of([PointAt(0, 10)]) });

        var lands = Plan(scene).Lands;

        Assert.Equal(edgeGoesBack ? new[] { PointAt(5, 5), PointAt(Last, 10) } : new[] { PointAt(5, 5) }, lands[0].Colours.Points);
        Assert.Equal(edgeGoesBack ? new[] { PointAt(0, 10) } : [], lands[1].Colours.Points);
    }

    [Fact]
    public void TheLandsDoNotDependOnTheNumberOfWorkers()
    {
        var random = new Random(11);
        var scene = new Scene();
        for (var index = 0; index < 40; index++)
        {
            var point = new Vector3(random.NextSingle() * 3 * ExteriorGrid.CellSize, random.NextSingle() * 2 * ExteriorGrid.CellSize, Raised);
            scene = random.Next(4) == 0 ? scene.Standing(point) : scene.Removed(point);
        }
        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 2; y++) scene = scene.Land(x, y, RaisedEverywhere(by: random.Next(1, 20) * 8f));
        }

        var one = Plan(scene, Settings with { TerrainEdgeBlendDistance = 2 * GridStep }, workers: 1);
        var eight = Plan(scene, Settings with { TerrainEdgeBlendDistance = 2 * GridStep }, workers: 8);

        Assert.Equal(one.Lands.Select(Describe), eight.Lands.Select(Describe));
        Assert.Contains(one.Lands, land => land.IsRestored);
    }

    private static string Describe(LandChangeJudged land) =>
        $"{land.Change.Cell} {land.Never} {string.Join(",", land.Quarters.Select(quarter => quarter.Verdict))} {string.Join(",", land.Heights.Points)} "
        + $"{string.Join(";", land.Kept.Select(kept => $"{kept.Reason}:{string.Join(",", kept.Points.Points)}"))} {string.Join(",", land.FinalHeights ?? [])} "
        + $"{string.Join(",", land.Beyond.West ?? [])} {string.Join(",", land.Beyond.East ?? [])} {string.Join(",", land.Beyond.South ?? [])} {string.Join(",", land.Beyond.North ?? [])}";

    private static LandPointsKept KeptFor(LandChangeJudged land, TerrainKeptReason reason) => Assert.Single(land.Kept, kept => kept.Reason == reason);

    private static int PointAt(int x, int y) => y * Side + x;

    /// <summary>The height on the 8-unit steps nearest to it, a half step rounding up: the steps of a record whose first point lies a whole number of steps up.</summary>
    private static float OnSteps(float height) => Step * MathF.Floor(height / Step + 0.5f);

    /// <summary>A small square around the world grid point (x, y), seen from above.</summary>
    private static (Vector2 Min, Vector2 Max) AreaAround(int x, int y) => (new Vector2(x * GridStep - 1, y * GridStep - 1), new Vector2(x * GridStep + 1, y * GridStep + 1));

    private static LandChangedByCleanedMod LandAt(int x, int y, LandChangedByCleanedMod land) =>
        land with { Cell = new ExteriorCell(Space, x, y), LandKey = new RecordKey(BaseGame, (uint)(0x700 + (x + 8) * 16 + y + 8)) };

    private static LandAreasJudged AllCleared(LandChangedByCleanedMod land)
    {
        var cleared = TestAreaShares.Quarter(AreaVerdict.Cleared);
        return new LandAreasJudged(land, Never: null, [cleared, cleared, cleared, cleared]);
    }

    /// <summary>A plan over the LAND records, given in the order of their cells, with every quarter cleared.</summary>
    private static TerrainUndoPlan PlanOf(float blendDistance, params LandChangedByCleanedMod[] lands) =>
        new([.. lands.Select(AllCleared)], _ => new LandHeights(null, null), HeightTolerance, blendDistance);

    private static PointSet PointsWhere(Func<int, int, bool> include) =>
        PointSet.Of(Enumerable.Range(0, TerrainGrid.VertexCount).Where(point => include(point % Side, point / Side)));

    private static float[] Heights(Func<int, int, float> heightAt) => [.. Enumerable.Range(0, TerrainGrid.VertexCount).Select(point => heightAt(point % Side, point / Side))];

    private static Vector3 QuarterCentreOf(int cellX, int cellY, int quarterX, int quarterY) =>
        new(cellX * ExteriorGrid.CellSize + (2 * quarterX + 1) * QuarterCentre, cellY * ExteriorGrid.CellSize + (2 * quarterY + 1) * QuarterCentre, Raised);

    /// <summary>A removed object of the cleaned mod at the centre of every quarter of the cell and its neighbour to the east.</summary>
    private static Scene ClearedAround(int cellX, int cellY)
    {
        var scene = new Scene();
        for (var x = cellX; x <= cellX + 1; x++)
        {
            foreach (var (quarterX, quarterY) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) }) scene = scene.Removed(QuarterCentreOf(x, cellY, quarterX, quarterY));
        }
        return scene;
    }

    /// <summary>A LAND record of the base game at cell (0, 0) the cleaned mod changed nothing of yet.</summary>
    private static LandChangedByCleanedMod Unchanged() => new(
        new ExteriorCell(Space, 0, 0),
        new RecordKey(BaseGame, 0x600),
        PointSet.Of([]),
        PointSet.Of([]),
        default,
        UndoRest: false,
        new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
        TestTargets.TargetName,
        new LandHeights(Heights((_, _) => 0f), Heights((_, _) => 0f)));

    /// <summary>The cleaned mod raised every point of the base game's flat ground.</summary>
    private static LandChangedByCleanedMod RaisedEverywhere(float by = Raised) =>
        Unchanged() with { UndoPoints = PointsWhere((_, _) => true), Heights = new LandHeights(Heights((_, _) => 0f), Heights((_, _) => by)) };

    private static RestoreWhereRemovedResult Plan(Scene scene, WhereRemovedRestoreSettings? settings = null, int workers = 1)
    {
        ImmutableArray<TargetObject> targets = [.. scene.Targets];
        var collected = TestScenes.CreateWorld(targets, []) with { ChangedLands = [.. scene.Lands], WinningPresence = scene.Presence };
        var rules = TestPlacedGuardRules.Create(
            Shapes,
            TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []),
            Plugins,
            new OtherModsObjectsSettings(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap: 1f, SupportLostFraction: 0.5f),
            TestGround.NoTerrain(),
            workers);
        var final = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. scene.Removals.Select(index => new ProposedRemoval(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
        var caches = new TestRunCaches(Shapes, navmeshes: NoNavmeshes, shownTargets: TestScenes.Create(targets, [], Shapes).IndexShownTargets);
        var restore = settings ?? Settings;
        return RestoreLoopReuse.Plan(
            new(
                new(collected, OtherModsObjectsResult.SwitchedOff),
                final,
                TestClearedAreas.Of(collected, final, caches, restore),
                new(rules.WorldCaches, rules.TooCloseRule(), rules.HoldersOfObjectsThatStay, TestNavmeshCheck.NoFault),
                TestTargetGroups.None,
                new(
                    caches,
                    new TestLandPlugin(space => space == ChildSpace ? Space : space, _ => new LandHeights(null, null)),
                    Plugins,
                    TestTargets.TargetName),
                new Execution(workers),
                restore,
                () => TimeSpan.Zero));
    }

    /// <summary>Target objects (each a block) with the ones removed, and the LAND facts, built up one by one.</summary>
    private sealed record Scene(ImmutableList<TargetObject> Targets, ImmutableList<int> Removals, ImmutableList<LandChangedByCleanedMod> Lands)
    {
        public Scene()
            : this([], [], [])
        {
        }

        /// <summary>The winning presence of the placed records that are removed or follow an enable parent.</summary>
        public ImmutableDictionary<RecordKey, PlacedPresence> Presence { get; init; } = ImmutableDictionary<RecordKey, PlacedPresence>.Empty;

        public Scene WithPresence(RecordKey record, PlacedPresence presence) => this with { Presence = Presence.SetItem(record, presence) };

        public Scene Removed(Vector3 position) => Standing(position) with { Removals = Removals.Add(Targets.Count) };

        public Scene Standing(Vector3 position, TargetOrigin origin = TargetOrigin.CleanedMod, RecordKey? space = null) =>
            this with { Targets = Targets.Add(TestTargets.Create(Targets.Count, TestTargets.At(position), Block.Base, space ?? Space) with { Origin = origin }) };

        public Scene Land(int x, int y, LandChangedByCleanedMod land) =>
            this with { Lands = Lands.Add(land with { Cell = new ExteriorCell(Space, x, y), LandKey = new RecordKey(BaseGame, 0x600 + (uint)(Lands.Count)) }) };
    }
}
