using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static ChangedTerrainScenes;

/// <summary>The changed-terrain step's water: the accepted water per cell, the worldspace defaults, the covered share and the cells without checked land.</summary>
public class ChangedTerrainWaterObjectsTests
{
    private const float BelowTheGround = -100f;
    private const float AboveTheGround = 50f;
    private const float High = 64f;

    /// <summary>The share of an area's vertices that must get covered or uncovered in <see cref="Settings"/>: 4 of the 16 at <see cref="WideSpot"/>.</summary>
    private const int CoverChangesAtShare = 4;

    /// <summary>A plugin's copy of the cell with water at its own height; null for no water.</summary>
    private static CellWaterCopy Water(PluginName plugin, float? level) => new(plugin, HasWater: level != null, level);

    private static CellWaterCopy WaterAtTheDefault(PluginName plugin) => new(plugin, HasWater: true, OwnLevel: null);

    private static WorldspaceWaterCopy Default(PluginName plugin, float? level) => new(plugin, level);

    /// <summary>The worldspace copies of a test that gives none: only the base game's.</summary>
    private static readonly WorldspaceWaterCopy[] BaseGameWorldspace = [Default(BaseGame, BelowTheGround)];

    private static ChangedTerrainPass FindWithWater(
        TerrainVersion[] terrain,
        CellWaterCopy[] water,
        ChangedTerrainSettings? settings = null,
        Vector3? position = null,
        WorldspaceWaterCopy[]? worldspace = null,
        RecordKey? space = null) =>
        Find(
            new TerrainPlugin(
                new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = terrain },
                water: new Dictionary<ExteriorCell, WaterCopies> { [Cell with { WorldspaceKey = space ?? Worldspace }] = new(water, worldspace ?? BaseGameWorldspace) }),
            settings ?? Settings(),
            threads: 1,
            Placed(0, Rock, position ?? Spot, space));

    [Fact]
    public void AnotherModRaisingTheWaterOverTheGroundRemovesTheObject() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AnotherModLoweringTheWaterBelowTheGroundRemovesTheObject() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, AboveTheGround), Water(Other, BelowTheGround)]).Proposals.AsEnumerable());

    [Theory]
    [InlineData(null, AboveTheGround, TerrainChangeKind.WaterAdded)]
    [InlineData(AboveTheGround, null, TerrainChangeKind.WaterRemoved)]
    public void AnotherModAddingOrRemovingWaterOverTheGroundRemovesTheObject(float? expected, float? shown, TerrainChangeKind kind) =>
        Assert.Equal([Removal(0, Other, kind)], FindWithWater([Flat(Target)], [Water(Target, expected), Water(Other, shown)]).Proposals.AsEnumerable());

    [Fact]
    public void UnchangedWaterRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(BaseGame, AboveTheGround), Water(Target, AboveTheGround), Water(Other, AboveTheGround)]).Proposals);

    [Fact]
    public void AWaterChangeThatLeavesTheGroundUnderWaterRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, AboveTheGround), Water(Other, 2 * AboveTheGround)]).Proposals);

    [Fact]
    public void WaterAddedBelowTheGroundRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, null), Water(Other, BelowTheGround)]).Proposals);

    /// <summary>The ground is at 0; the water moves from just below it to just above it by the given change.</summary>
    [Theory]
    [InlineData(Tolerance, Tolerance, false)]
    [InlineData(Tolerance, Tolerance + HeightStep, true)]
    [InlineData(0f, HeightStep, true)]
    public void AWaterHeightChangeCountsOnlyBeyondItsTolerance(float tolerance, float change, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, -1f), Water(Other, change - 1f)], Settings(waterTolerance: tolerance));

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void WaterHeightChangesCountOnlyWhenSwitchedOn() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], Settings(waterHeights: false)).Proposals);

    [Fact]
    public void WaterAddedOrRemovedCountsOnlyWhenSwitchedOn() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, null), Water(Other, AboveTheGround)], Settings(waterAddedOrRemoved: false)).Proposals);

    [Fact]
    public void WithNoWaterChangeCountedTheWaterIsNotRead()
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] },
            water: new Dictionary<ExteriorCell, WaterCopies> { [Cell] = new([Water(Target, BelowTheGround), Water(Other, AboveTheGround)], BaseGameWorldspace) });

        Assert.Empty(Find(plugin, Settings(waterHeights: false, waterAddedOrRemoved: false)).Proposals);
        Assert.Equal(0, plugin.WaterReads);
    }

    /// <summary>The ground of the area at <see cref="WideSpot"/> is high except at the given number of vertices, which the raised water covers.</summary>
    [Theory]
    [InlineData(CoverChangesAtShare - 1, false)]
    [InlineData(CoverChangesAtShare, true)]
    public void AWaterChangeRemovesTheObjectOnlyFromTheShareOfCoveredOrUncoveredVerticesOn(int covered, bool removed)
    {
        var land = new TerrainVersion(Target, Heights(High, [.. WideSpotVertices.Take(covered).Select(vertex => (vertex.Row * TerrainGrid.VerticesPerSide + vertex.Column, Ground))]), Layers: null);

        var result = FindWithWater([land], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], position: WideSpot);

        Assert.Equal(removed, result.Proposals.Any());
    }

    /// <summary>
    /// The other mod adds the land deep down; OwnChanges.esp raises it high, except at the given number of vertices of the area, which
    /// the raised water covers. Every vertex has an accepted height and none an accepted terrain type, and all stay in the total.
    /// </summary>
    [Theory]
    [InlineData(CoverChangesAtShare - 1, false)]
    [InlineData(CoverChangesAtShare, true)]
    public void VerticesWithAnAcceptedHeightButNoAcceptedTerrainTypeStayInTheWaterShare(int covered, bool removed)
    {
        var low = WideSpotVertices.Take(covered).Select(vertex => (vertex.Row * TerrainGrid.VerticesPerSide + vertex.Column, Ground));
        TerrainVersion[] land = [new(Other, Heights(Deep), Layers: null), new(OwnChanges, Heights(High, [.. low]), Layers: null)];

        var result = FindWithWater(land, [Water(Target, BelowTheGround), Water(Later, AboveTheGround)], position: WideSpot);

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void LandLoweredUnderUnchangedWaterIsAHeightChangeOnly() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.Height)],
            FindWithWater([new TerrainVersion(Target, Heights(High), Layers: null), Flat(Other)], [Water(Target, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>
    /// The water rises, but stays below the expected ground; only the lowered ground puts the point under water, so the water
    /// change is not counted and the height check alone decides.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void APointCoveredOnlyThroughTheLandChangeIsLeftToTheHeightCheck(bool heights, bool removed)
    {
        var result = FindWithWater(
            [new TerrainVersion(Target, Heights(High), Layers: null), Flat(Other)],
            [Water(Target, AboveTheGround - Tolerance - HeightStep), Water(Other, AboveTheGround)],
            Settings(heights: heights));

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.Height)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Fact]
    public void AHeightAndWaterChangeHasBothKindsAndNamesTheLaterMod() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.Height | TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target), RaisedAt(Other, Near, -Raised)], [Water(Target, BelowTheGround), Water(Later, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AWaterChangeOnlyByAPluginRequiringTheTargetCountsForNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(RequiringMod, AboveTheGround)]).Proposals);

    [Fact]
    public void AnUntrustedModsWaterCarriedByATrustedModIsAccepted() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(TrustedMod, AboveTheGround)]).Proposals);

    [Fact]
    public void AnUntrustedModsDefaultCarriedByATrustedModIsAccepted() =>
        Assert.Empty(
            FindWithWater(
                [Flat(Target)],
                [WaterAtTheDefault(Target)],
                worldspace: [Default(BaseGame, BelowTheGround), Default(Other, AboveTheGround), Default(TrustedMod, AboveTheGround)]).Proposals);

    [Fact]
    public void ThePatchCarryingAnUntrustedModsWaterNamesThatMod() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(Patch, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>Only the patch holds the change: an earlier patcher of the group made it, and no plugin is named.</summary>
    [Fact]
    public void AWaterChangeOnlyThePatchHoldsNamesAnEarlierPatcher() =>
        Assert.Equal(
            [Removal(0, changedBy: null, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Patch, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>The trusted mod raises the water and an earlier patcher puts the target's level back: the level is accepted from the target.</summary>
    [Fact]
    public void AWaterLevelThePatchPutsBackToALowerTrustedVersionsIsNoChange() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(TrustedMod, AboveTheGround), Water(Patch, BelowTheGround)]).Proposals);

    /// <summary>
    /// OwnChanges.esp re-saves the other mod's water: one height step above it is within the tolerance, so it owns no water height
    /// there and the other mod is named; twice the other mod's height is its own, which is accepted.
    /// </summary>
    [Theory]
    [InlineData(AboveTheGround + HeightStep, true)]
    [InlineData(2 * AboveTheGround, false)]
    public void AModTrustedForItsOwnChangesOwnsAWaterHeightOnlyBeyondTheTolerance(float resaved, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(OwnChanges, resaved)]);

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.WaterHeight)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// Only untrusted mods have the cell, as in a world one of them adds, so its water has no accepted value: it is not checked, and
    /// nothing about it is reported. The land is still compared, and the object is not one without terrain.
    /// </summary>
    [Fact]
    public void ACellNoTrustedPluginHasIsNotCheckedForWater()
    {
        var result = FindWithWater([Flat(Target)], [Water(Other, BelowTheGround), Water(Later, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>The cell uses the default water height, and only an untrusted mod has the worldspace: the default has no accepted value.</summary>
    [Fact]
    public void ACellUsingADefaultNoTrustedPluginHasIsNotCheckedForWater()
    {
        var result = FindWithWater([Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(Other, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// Only the other mod has the cell's land, so no vertex is checked, but the target has the cell's water: the water is compared over
    /// the ground as the game shows it. An object on that ground reaches it; one high above it reaches it only at any height.
    /// </summary>
    [Theory]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, Ground, true)]
    [InlineData(TerrainCheckedObjects.VisibleObjectsAtAnyHeight, Ground, true)]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, 1000f, false)]
    [InlineData(TerrainCheckedObjects.VisibleObjectsAtAnyHeight, 1000f, true)]
    public void WaterIsComparedInACellWhoseLandOnlyUntrustedModsHave(TerrainCheckedObjects objects, float objectHeight, bool removed)
    {
        var result = FindWithWater([Flat(Other)], [Water(Target, null), Water(Later, AboveTheGround)], Settings(objects), Spot with { Z = objectHeight });

        ProposedRemoval[] expected = removed ? [Removal(0, Later, TerrainChangeKind.WaterAdded)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
        Assert.Equal((0, 1, 0), (result.CellsCompared, result.CellsComparedForWaterOnly, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// The evidence of a water finding names the points the water covered, over the shown ground (only an untrusted mod has the land)
    /// and over accepted ground alike, so that a restore that would change those heights keeps them.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWaterFindingNamesThePointsItCovered(bool landAccepted)
    {
        var hit = Assert.Single(FindWithWater([Flat(landAccepted ? Target : Other)], [Water(Target, null), Water(Later, AboveTheGround)]).Hits);

        var counted = Assert.Single(hit.Area);
        Assert.NotEmpty(counted.WaterFlipped);
        Assert.Contains(Near, counted.WaterFlipped);
        Assert.Equal(counted.WaterFlipped.Order(), counted.WaterFlipped);
    }

    /// <summary>
    /// The object's area crosses a cell edge. It hangs 300 units up, high above the west cell's ground, which the target has and the
    /// other mod raises a little; it reaches only the east cell's ground, whose land only the other mod has and whose water the target
    /// has. The east cell's points decide the reach only for its own water: the raise in the west counts only at any height, and a
    /// water change in the east counts on its own.
    /// </summary>
    [Theory]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, false, null)]
    [InlineData(TerrainCheckedObjects.VisibleObjectsAtAnyHeight, false, TerrainChangeKind.Height)]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, true, TerrainChangeKind.WaterHeight)]
    public void PointsOfACellCheckedForWaterOnlyDecideTheReachOnlyForItsOwnWater(TerrainCheckedObjects objects, bool eastWaterRaised, TerrainChangeKind? kind)
    {
        const float Hanging = 300f;
        var east = new ExteriorCell(Worldspace, 1, 0);
        var westRaised = 8 * TerrainGrid.VerticesPerSide + TerrainGrid.SquaresPerSide;
        CellWaterCopy[] eastWater = eastWaterRaised ? [Water(Target, BelowTheGround), Water(Later, Hanging + AboveTheGround)] : [Water(Target, BelowTheGround)];
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]>
            {
                [Cell] = [Flat(Target), RaisedAt(Other, westRaised)],
                [east] = [new TerrainVersion(Other, Heights(Hanging), Layers: null)],
            },
            water: new Dictionary<ExteriorCell, WaterCopies> { [east] = new(eastWater, BaseGameWorldspace) });

        var result = Find(plugin, Settings(objects), threads: 1, Placed(0, Rock, new Vector3(ExteriorGrid.CellSize, Spot.Y, Hanging)));

        ProposedRemoval[] expected = kind is { } changed ? [Removal(0, changed == TerrainChangeKind.Height ? Other : Later, changed)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Fact]
    public void WithNoWaterSwitchOnACellWithoutCheckedLandIsNotChecked()
    {
        var result = FindWithWater([Flat(Other)], [Water(Target, null), Water(Later, AboveTheGround)], Settings(waterHeights: false, waterAddedOrRemoved: false));

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 0, 1), (result.CellsCompared, result.CellsComparedForWaterOnly, result.ObjectsWithoutTerrain));
    }

    /// <summary>The cell has no land record at all, only trusted water that a later mod adds: it is not checked.</summary>
    [Fact]
    public void ACellWithoutALandRecordIsNotCheckedForWater()
    {
        var result = FindWithWater([], [Water(Target, null), Water(Later, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 0, 1), (result.CellsCompared, result.CellsComparedForWaterOnly, result.ObjectsWithoutTerrain));
    }

    [Fact]
    public void ACellWhoseLandAndWaterOnlyUntrustedModsHaveIsNotChecked()
    {
        var result = FindWithWater([Flat(Other)], [Water(Other, BelowTheGround), Water(Later, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 1), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>The only land record of the cell holds no heights, so there is no ground to compare the water over.</summary>
    [Fact]
    public void ACellWhoseOnlyLandHoldsNoHeightsIsNotCheckedForWater()
    {
        var result = FindWithWater([new TerrainVersion(Other, Heights: null, Layers: null)], [Water(Target, null), Water(Later, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 1), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// Only untrusted mods have the cell's land; the later mod lowers it under the target's unchanged water. The ground is taken as
    /// shown, so the point flips through no water change and the land change is not compared.
    /// </summary>
    [Fact]
    public void InACellWhoseLandOnlyUntrustedModsHaveOnlyTheWaterCanCoverAPoint()
    {
        var result = FindWithWater([new TerrainVersion(Other, Heights(High), Layers: null), Flat(Later)], [Water(Target, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// The object's area crosses a cell edge, six vertices on each side. The raised water covers the six in the west cell, whose land
    /// the target has; the east cell's land, which only the other mod has, stays dry, and its six vertices count in the total.
    /// </summary>
    [Theory]
    [InlineData(50, true)]
    [InlineData(75, false)]
    public void TheWaterShareCountsTheVerticesOfACellWhoseLandOnlyUntrustedModsHave(int waterPercent, bool removed)
    {
        var east = new ExteriorCell(Worldspace, 1, 0);
        CellWaterCopy[] raisedWater = [Water(Target, BelowTheGround), Water(Later, AboveTheGround)];
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)], [east] = [new TerrainVersion(Other, Heights(High), Layers: null)] },
            water: new Dictionary<ExteriorCell, WaterCopies> { [Cell] = new(raisedWater, BaseGameWorldspace), [east] = new(raisedWater, BaseGameWorldspace) });

        var result = Find(plugin, Settings(waterPercent: waterPercent), threads: 1, Placed(0, Rock, new Vector3(ExteriorGrid.CellSize, Spot.Y, 0)));

        Assert.Equal(removed, result.Proposals.Any());
        Assert.Equal(1, result.CellsCompared);
    }

    /// <summary>The accepted cell has no water, so its worldspace's default is not needed, though no trusted plugin has the worldspace.</summary>
    [Fact]
    public void ACellWithoutWaterIsCheckedWhateverItsWorldspaceHas() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.WaterAdded)],
            FindWithWater([Flat(Target)], [Water(Target, null), Water(Later, AboveTheGround)], worldspace: [Default(Other, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedModCarryingTheLastTrustedCellCopysWaterChangesNothing() =>
        Assert.Empty(FindWithWater([Flat(BaseGame)], [Water(BaseGame, BelowTheGround), Water(Master, AboveTheGround), Water(Early, AboveTheGround)]).Proposals);

    [Fact]
    public void AModLoadingBeforeTheTargetDoesNotChangeTheTargetsWater() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Early, AboveTheGround), Water(Target, BelowTheGround)]).Proposals);

    /// <summary>The target's copy of the worldspace and none of the cell, as for a neighbour cell reached by the margin.</summary>
    private static readonly WorldspaceWaterCopy[] TargetWorldspace = [Default(BaseGame, BelowTheGround), Default(Target, BelowTheGround)];

    [Fact]
    public void TheMastersCellCopyIsAcceptedWhenTheTargetHasOnlyAWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Master, BelowTheGround), Water(Other, AboveTheGround)], worldspace: TargetWorldspace).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedCellCopyIsNotAcceptedThroughTheTargetsWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Early, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Master, BelowTheGround), Water(Early, AboveTheGround)], worldspace: TargetWorldspace).Proposals.AsEnumerable());

    [Fact]
    public void ALaterModCarryingTheMastersWaterChangesNothing() =>
        Assert.Empty(
            FindWithWater(
                [Flat(Target)], [Water(Master, BelowTheGround), Water(Early, AboveTheGround), Water(Later, BelowTheGround)], worldspace: TargetWorldspace).Proposals);

    [Fact]
    public void AChangedWorldspaceDefaultNamesThePluginOfTheWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>The water is read in the object's own worldspace, whose land comes from its parent; the copies then hold the parent's defaults.</summary>
    [Fact]
    public void AChangedDefaultOfAWorldspaceSuppliedToAChildNamesThePluginOfTheSupplyingWorldspace() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)], space: ChildWorldspace)
                .Proposals.AsEnumerable());

    /// <summary>The worldspace stores no default water height, so the target's cell using the default has no water.</summary>
    [Fact]
    public void ACellUsingTheDefaultOfAWorldspaceWithoutOneHasNoWater() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target), Water(Other, AboveTheGround)], worldspace: [Default(BaseGame, level: null)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AWorldspaceVersionAddingTheDefaultAddsWaterToCellsUsingIt() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(BaseGame, level: null), Default(Other, AboveTheGround)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AWorldspaceVersionDroppingTheDefaultRemovesTheWaterOfCellsUsingIt() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterRemoved)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(BaseGame, AboveTheGround), Default(Other, level: null)])
                .Proposals.AsEnumerable());

    /// <summary>The other mod switches the cell from the worldspace's default to its own height, or back.</summary>
    [Theory]
    [InlineData(null, AboveTheGround)]
    [InlineData(AboveTheGround, null)]
    public void SwitchingBetweenTheOwnHeightAndTheDefaultIsAWaterHeightChange(float? targetOwnLevel, float? otherOwnLevel) =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [new CellWaterCopy(Target, HasWater: true, targetOwnLevel), new CellWaterCopy(Other, HasWater: true, otherOwnLevel)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AMissingDefaultDoesNotMatterToACellWithItsOwnHeight() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], worldspace: [Default(BaseGame, level: null)])
                .Proposals.AsEnumerable());

    /// <summary>A plugin with two cells at the grid has two copies in load order; the last is the one the game shows.</summary>
    [Theory]
    [InlineData(AboveTheGround, BelowTheGround, false)]
    [InlineData(BelowTheGround, AboveTheGround, true)]
    public void OfTwoCellCopiesInOnePluginTheLastIsShown(float first, float last, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, first), Water(Other, last)]);

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void ACellWithoutTheHasWaterFlagHasNoWaterWhateverHeightItStores() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater([Flat(Target)], [new CellWaterCopy(Target, HasWater: false, AboveTheGround), Water(Other, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>
    /// The other mod gives the cell its own height; the later mod's changed default is not what the shown water is made of, so the
    /// later mod is not named though it loads later.
    /// </summary>
    [Fact]
    public void ADefaultTheShownCellDoesNotUseIsNotNamed() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target), Water(Other, AboveTheGround)], worldspace: [.. TargetWorldspace, Default(Later, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>The other mod switches the cell to the default, which the later mod changed: both made the shown water, the later is named.</summary>
    [Fact]
    public void WhenTheCellAndTheDefaultItUsesBothChangedTheLaterLoadingModIsNamed() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [Water(Target, BelowTheGround), WaterAtTheDefault(Other)], worldspace: [.. TargetWorldspace, Default(Later, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>The trusted mod's cell uses the default, which only the untrusted mod's worldspace version changed.</summary>
    [Fact]
    public void ATrustedCellUsingTheDefaultDoesNotMakeAnUntrustedDefaultAccepted() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)],
                [WaterAtTheDefault(Target), WaterAtTheDefault(Other), WaterAtTheDefault(TrustedMod)],
                worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>Both worldspaces use the same land cell, which is compared once; each has its own cell water.</summary>
    [Fact]
    public void ObjectsInAChildAndItsParentAtOneGridShareTheLandAndKeepTheirOwnWater()
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] },
            water: new Dictionary<ExteriorCell, WaterCopies>
            {
                [Cell] = new([Water(Target, BelowTheGround), Water(Other, BelowTheGround)], BaseGameWorldspace),
                [Cell with { WorldspaceKey = ChildWorldspace }] = new([Water(Target, BelowTheGround), Water(Other, AboveTheGround)], BaseGameWorldspace),
            });

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock), Placed(1, Rock, space: ChildWorldspace));

        Assert.Equal([Removal(1, Other, TerrainChangeKind.WaterHeight)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
    }
}
