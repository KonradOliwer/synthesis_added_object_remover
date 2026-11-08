using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static ChangedTerrainScenes;

/// <summary>
/// The changed-terrain step: the accepted height per vertex, which plugin is named for a change, the check area, the height rule,
/// which objects are checked, the per-object error boundary and the independence from the thread count.
/// </summary>
public class ChangedTerrainObjectsTests
{
    [Fact]
    public void ASwitchedOffStepReadsNoTerrainAndProposesNothing()
    {
        var world = TestScenes.CreateWorld([Placed(0, Rock)], []);

        var result = Find(world, Mods.AcceptedWorld, new PluginNameParsingPlugin(), threads: 1, TestJudgedGroups.OfAll(world.Targets, Settings() with { On = false }));

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Proposals);
    }

    /// <summary>The same terrain: a change in one accepted world is the accepted value in another where its plugin is believed.</summary>
    [Theory]
    [InlineData(VersionTrust.None, true)]
    [InlineData(VersionTrust.Full, false)]
    public void TheSameTerrainChangesTheObjectOnlyWhenTheChangerIsNotAcceptedInTheWorld(VersionTrust otherTrust, bool removed)
    {
        var world = Mods.AcceptedWorld with { Plugins = Mods.AcceptedWorld.Plugins.SetItem(Other, otherTrust) };
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };

        var collected = TestScenes.CreateWorld([Placed(0, Rock)], []);

        var result = Find(collected, world, new TerrainPlugin(terrain), threads: 1, TestJudgedGroups.OfAll(collected.Targets, Settings()));

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void AnotherModRaisingTheTerrainUnderTheObjectRemovesIt()
    {
        var world = TestScenes.CreateWorld([Placed(0, Rock)], []);
        var terrain = new TerrainPlugin(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] });

        var decided = Decide(world, Mods.AcceptedWorld, terrain, threads: 1, TestJudgedGroups.OfAll(world.Targets, Settings()));
        var result = decided.Result.Passes.Single();

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Equal([Removal(0, Other)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
        Assert.Equal(new Round(1, RoundKind.ChangedTerrain), Assert.Single(decided.Decisions.Rounds));
        Assert.True(decided.Decisions.IsRemoved(new TargetId(0)));
    }

    [Fact]
    public void AFindingNamesTheVerticesWhoseChangeCounted()
    {
        var hit = Assert.Single(FindInCell(Flat(Target), RaisedAt(Other, Near)).Hits);

        var counted = Assert.Single(hit.Area);
        Assert.Equal((Cell, Cell, true), (counted.LandCell, counted.WaterCell, counted.WaterFlipped.IsEmpty));
        Assert.Equal([Near], counted.HeightVertices.AsEnumerable());
        Assert.Empty(counted.TypeVertices);
    }

    /// <summary>
    /// Of an object at <see cref="Spot"/>, one in the next cell east and one at the spot whose settings are off, only the first reads
    /// the land of the cell the spot lies in.
    /// </summary>
    [Fact]
    public void TheObjectsReadingACellAreThoseWhoseCheckedAreasReachIt()
    {
        var view = ChangedTerrainRules.ViewOfLoadOrder(new TerrainPlugin(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] }), restored: []);
        var rule =ChangedTerrainRules.CreateJudging(Mods.AcceptedWorld, LoadOrder, view, new BoxShapes());
        (TargetObject, ChangedTerrainSettings)[] objects =
        [
            (Placed(0, Rock), Settings()),
            (Placed(1, Rock, Spot + new Vector3(ExteriorGrid.CellSize, 0, 0)), Settings()),
            (Placed(2, Rock), Settings() with { On = false }),
        ];

        Assert.Equal([0], rule.FindObjectsReading(objects, new HashSet<ExteriorCell> { Cell }).AsEnumerable());
    }

    /// <summary>Every object stands at <see cref="Spot"/>, over terrain that Other.esp raised by <see cref="Raised"/>.</summary>
    private static ChangedTerrainResult FindGroups(int threads, params ObjectsJudgedWith<ChangedTerrainSettings>[] groups)
    {
        var plugin = new TerrainPlugin(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] });
        var world = TestScenes.CreateWorld([Placed(0, Rock), Placed(1, Rock), Placed(2, Rock), Placed(3, Rock)], []);
        return Find(world, Mods.AcceptedWorld, plugin, threads, groups);
    }

    [Fact]
    public void GroupsWithDifferentSettingsRunOnceEachOverTheirOwnObjects()
    {
        var result = FindGroups(
            threads: 1,
            TestJudgedGroups.Of([0, 2], Settings()),
            TestJudgedGroups.Of([1], Settings(tolerance: Raised / 2)),
            TestJudgedGroups.Of([3], Settings(tolerance: Raised)));

        Assert.Equal(["0,2", "1", "3"], result.Passes.Select(pass => string.Join(",", pass.Objects.Select(target => target.Index))));
        Assert.Equal(["0,2", "1", ""], result.Passes.Select(pass => string.Join(",", pass.Hits.Select(hit => hit.Target.Index))));
        Assert.Equal([Removal(0, Other), Removal(1, Other), Removal(2, Other)], result.Proposals.AsEnumerable());
        Assert.All(result.Passes, pass => Assert.Equal(1, pass.CellsCompared));
    }

    [Fact]
    public void GroupsWithEqualSettingsAreMergedIntoOneRunOverAllTheirObjects()
    {
        var result = FindGroups(threads: 1, TestJudgedGroups.Of([2, 0], Settings()), TestJudgedGroups.Of([1], Settings()));

        var pass = Assert.Single(result.Passes);
        Assert.Equal([0, 1, 2], pass.Objects.Select(target => target.Index));
        Assert.Equal([Removal(0, Other), Removal(1, Other), Removal(2, Other)], result.Proposals.AsEnumerable());
        Assert.Equal(1, pass.CellsCompared);
    }

    [Fact]
    public void AGroupWhoseStepIsOffReadsNothingWhileTheOthersRun()
    {
        var result = FindGroups(threads: 1, TestJudgedGroups.Of([0], Settings() with { On = false }), TestJudgedGroups.Of([1], Settings()));

        Assert.Equal([StepOutcome.SwitchedOff, StepOutcome.Performed], result.Passes.Select(pass => pass.Outcome));
        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Equal([Removal(1, Other)], result.Proposals.AsEnumerable());
    }

    [Fact]
    public void TheGroupsFindTheSameWithOneThreadAndWithMany()
    {
        ObjectsJudgedWith<ChangedTerrainSettings>[] groups =
        [
            TestJudgedGroups.Of([0, 2], Settings()),
            TestJudgedGroups.Of([1, 3], Settings(tolerance: Raised / 2)),
        ];

        var one = FindGroups(1, groups);
        var many = FindGroups(4, groups);

        Assert.Equal(one.Passes.Select(pass => pass.Hits.Select(Describe).ToList()), many.Passes.Select(pass => pass.Hits.Select(Describe).ToList()));
        Assert.Equal(one.Proposals.AsEnumerable(), many.Proposals.AsEnumerable());
    }

    /// <summary>A finding with its evidence spelled out, so two findings compare by value.</summary>
    private static string Describe(ObjectOnChangedTerrain hit) =>
        $"{hit.Target} {hit.ChangedBy} {hit.Kind} " + string.Join(
            "; ",
            hit.Area.Select(cell => $"{cell.LandCell} {cell.WaterCell} [{string.Join(",", cell.HeightVertices)}] [{string.Join(",", cell.TypeVertices)}] {!cell.WaterFlipped.IsEmpty}"));

    [Fact]
    public void AChangeOutsideTheCheckAreaCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Far)).Proposals);

    [Theory]
    [InlineData(Tolerance, Tolerance, false)]
    [InlineData(Tolerance, Tolerance + HeightStep, true)]
    [InlineData(0f, HeightStep, true)]
    public void AHeightChangeCountsOnlyBeyondTheTolerance(float tolerance, float change, bool removed)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near, change)] };

        Assert.Equal(removed, Find(terrain, Settings(tolerance: tolerance)).Proposals.Any());
    }

    [Fact]
    public void APluginRequiringTheTargetsOwnChangeCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(RequiringMod, Near)).Proposals);

    [Fact]
    public void APluginRequiringTheTargetsChangeCarriedByAnUntrustedModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(RequiringMod, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void ATrustedModsChangeCarriedByAnUntrustedModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(TrustedMod, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void AnUntrustedModsHeightCarriedByATrustedCompatibilityPatchIsAccepted() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(CompatibilityPatch, Near)).Proposals);

    /// <summary>The carried height is the accepted one, so the later mod is named, not the mod whose height was carried.</summary>
    [Fact]
    public void AChangeOnTopOfACarriedHeightIsComparedWithTheCarriedHeight() =>
        Assert.Equal(
            [Removal(0, Later)],
            FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(CompatibilityPatch, Near), RaisedAt(Later, Near, 2 * Raised)).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedModCarryingAnotherUntrustedModsChangeLeavesTheFirstNamed() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Later, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ATrustedModsSmallChangeOnTopOfAnUntrustedChangeMakesItAccepted() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(TrustedMod, Near, Raised + HeightStep)).Proposals);

    /// <summary>
    /// OwnChanges.esp re-saves the other mod's land: one height step above the other mod's is within the tolerance, so it owns no
    /// height there and the other mod is named; twice the raise is its own height, which is accepted.
    /// </summary>
    [Theory]
    [InlineData(Raised + HeightStep, true)]
    [InlineData(2 * Raised, false)]
    public void AModTrustedForItsOwnChangesOwnsAHeightOnlyBeyondTheTolerance(float resaved, bool removed)
    {
        var result = FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(OwnChanges, Near, resaved));

        ProposedRemoval[] expected = removed ? [Removal(0, Other)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// Each step is within the tolerance of the version below, so OwnChanges.esp owns no height; together they are beyond it, and
    /// OwnChanges.esp holds the first height that differs from the accepted one beyond the tolerance.
    /// </summary>
    [Fact]
    public void SmallStepsAddingUpThroughAModTrustedForItsOwnChangesNameThatMod() =>
        Assert.Equal(
            [Removal(0, OwnChanges)],
            FindInCell(Flat(Target), RaisedAt(Other, Near, Tolerance), RaisedAt(OwnChanges, Near, Tolerance + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void ATrustedModsOwnLargerChangeOnTopOfAnUntrustedChangeCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(TrustedMod, Near, 2 * Raised)).Proposals);

    /// <summary>Each step is within the tolerance of the next, and only the trusted mod's own height is beyond it.</summary>
    [Fact]
    public void ATrustedModsStepPushingSmallChangesBeyondTheToleranceCountsForNothing() =>
        Assert.Empty(
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(CompatibilityPatch, Near, Tolerance), RaisedAt(TrustedMod, Near, Tolerance + HeightStep))
                .Proposals);

    /// <summary>Each step is within the tolerance of the next, and the later untrusted mod's own height is beyond it.</summary>
    [Fact]
    public void UntrustedModsSmallStepsAddingUpBeyondTheToleranceNameTheModWhoseOwnChangeExceedsIt() =>
        Assert.Equal(
            [Removal(0, Later)],
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(Later, Near, Tolerance + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void APatchTakingAnEarlierChangeBackInNamesTheModThatMadeIt() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AModUndoingTheChangeOfAPluginRequiringTheTargetChangesTheAcceptedTerrain() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(RequiringMod, Near), Flat(Other)).Proposals.AsEnumerable());

    [Fact]
    public void ALaterModRestoringTheAcceptedTerrainLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later)).Proposals);

    [Fact]
    public void ATrustedModRestoringTheAcceptedTerrainLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(TrustedMod)).Proposals);

    [Fact]
    public void AModLoadingBeforeTheCleanedModIsOverriddenByIt() =>
        Assert.Empty(FindInCell(RaisedAt(Early, Near), Flat(Target)).Proposals);

    [Fact]
    public void ThePatchCarryingAnUntrustedModsChangeNamesThatMod() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AChangeOnlyByTrustedModsAndThePatchRemovesNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(TrustedMod, Near), RaisedAt(Patch, Near)).Proposals);

    /// <summary>Only the patch holds the change: an earlier patcher of the group made it, and no plugin is named.</summary>
    [Fact]
    public void AChangeOnlyThePatchHoldsNamesAnEarlierPatcher() =>
        Assert.Equal([Removal(0, changedBy: null)], FindInCell(Flat(Target), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    /// <summary>An earlier patcher puts the target's height back over the trusted mod's raise: the height is accepted from the target.</summary>
    [Fact]
    public void AHeightThePatchPutsBackToALowerTrustedVersionsIsAcceptedFromThatVersion()
    {
        TerrainVersion[] versions = [Flat(Target), RaisedAt(TrustedMod, Near), Flat(Patch)];

        var terrain = Comparison().Compare(versions, []);

        Assert.Equal(Ground, terrain!.AcceptedHeights![Near]);
        Assert.Null(terrain.Changes);
        Assert.Empty(FindInCell(versions).Proposals);
    }

    /// <summary>
    /// An earlier run gave the patch the other mod's heights back: as long as the patch still holds them, the height is the world
    /// without that run's cleaned mod, not a change, whatever the other mod's trust here.
    /// </summary>
    [Theory]
    [InlineData(Raised, false)]
    [InlineData(2 * Raised, true)]
    public void AHeightThePatchWasGivenBackIsAcceptedWhileThePatchStillHoldsIt(float patchHeight, bool removed)
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near), RaisedAt(Later, Near, 3 * Raised), RaisedAt(Patch, Near, patchHeight)] },
            cellsOfRecords: new Dictionary<RecordKey, ExteriorCell> { [EarlierRunsLand] = Cell });

        var result = FindAfterEarlierRuns(plugin, [GivenBack(EarlierRunsLand, new RestoredPartName.LandHeights(PointSet.Of([Near])), Other)]);

        ProposedRemoval[] expected = removed ? [Removal(0, changedBy: null)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// The other mod's hill at <see cref="Near"/>, which the patch carries as a copy of the winning version, is accepted only when the
    /// earlier run gave that point back; heights it gave back elsewhere in the same LAND record accept nothing here.
    /// </summary>
    [Theory]
    [InlineData(Near, false)]
    [InlineData(Far, true)]
    public void AHeightThePatchHoldsIsAcceptedOnlyAtThePointsAnEarlierRunGaveBack(int givenBackPoint, bool removed)
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near), RaisedAt(Patch, Near)] },
            cellsOfRecords: new Dictionary<RecordKey, ExteriorCell> { [EarlierRunsLand] = Cell });

        var result = FindAfterEarlierRuns(plugin, [GivenBack(EarlierRunsLand, new RestoredPartName.LandHeights(PointSet.Of([givenBackPoint])), Other)]);

        ProposedRemoval[] expected = removed ? [Removal(0, Other)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// A height an earlier run gave back from the plugin this run cleans up is that plugin's own value, which this run trusts in full,
    /// so the given-back part changes no outcome, whether the patch still holds it or not.
    /// </summary>
    [Theory]
    [InlineData(Raised)]
    [InlineData(3 * Raised)]
    public void AHeightGivenBackFromThePluginThisRunCleansUpChangesNoOutcome(float patchHeight)
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(BaseGame), RaisedAt(Target, Near), RaisedAt(Later, Near, 2 * Raised), RaisedAt(Patch, Near, patchHeight)] },
            cellsOfRecords: new Dictionary<RecordKey, ExteriorCell> { [EarlierRunsLand] = Cell });

        var withThePart = FindAfterEarlierRuns(plugin, [GivenBack(EarlierRunsLand, new RestoredPartName.LandHeights(PointSet.Of([Near])), Target)]);

        Assert.Equal(FindAfterEarlierRuns(plugin, []).Proposals.AsEnumerable(), withThePart.Proposals.AsEnumerable());
    }

    /// <summary>At the cell's first and last grid point, the edge points a LAND record shares with its neighbours, a height is accepted only where the earlier run gave it back.</summary>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, VertexCount - 1, true)]
    [InlineData(VertexCount - 1, VertexCount - 1, false)]
    [InlineData(VertexCount - 1, 0, true)]
    public void AHeightThePatchHoldsAtAnEdgePointIsAcceptedOnlyWhereAnEarlierRunGaveItBack(int raisedPoint, int givenBackPoint, bool changed)
    {
        var restored = new[] { GivenBack(EarlierRunsLand, new RestoredPartName.LandHeights(PointSet.Of([givenBackPoint])), Other) };

        var terrain = Comparison().Compare([Flat(Target), RaisedAt(Other, raisedPoint), RaisedAt(Patch, raisedPoint)], restored);

        Assert.Equal(changed, terrain!.Changes?[raisedPoint].HeightChanger is not null);
    }

    /// <summary>
    /// An earlier run gave back every height of a LAND record no trusted plugin has: a given-back value counts only where some version
    /// is trusted, so no point becomes checked, and the ground stays taken as the game shows it, with only the cell's water compared.
    /// </summary>
    [Fact]
    public void HeightsGivenBackInACellNoTrustedPluginHasLeaveItsGroundAsShown()
    {
        var restored = new[] { GivenBack(EarlierRunsLand, new RestoredPartName.LandHeights(PointSet.Of(Enumerable.Range(0, VertexCount))), Other) };

        var terrain = Comparison().Compare([Flat(Other), Flat(Patch)], restored);

        Assert.True(terrain!.ShownOnly);
    }

    /// <summary>
    /// The earlier run cleaned up the later mod and gave the patch the other mod's layers of the quarter under the object back. The
    /// patch's road there, the other mod's, is accepted only where the later mod had changed the ground type, so the restore changed it
    /// back; where the later mod changed another part of the quarter only, the road is the other mod's change.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AGroundTypeThePatchHoldsIsAcceptedOnlyWhereTheEarlierRunChangedIt(bool laterChangedTheObjectsVertices, bool removed)
    {
        var road = RoadAtWideSpot(ChangesAtTypeShare);
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]>
            {
                [Cell] =
                [
                    FlatPainted(Target, Painted(Grass)),
                    FlatPainted(Other, road),
                    FlatPainted(Later, laterChangedTheObjectsVertices ? Painted(Grass) : RoadAtWideSpotAndQuarterCorner(ChangesAtTypeShare)),
                    FlatPainted(Patch, road),
                ],
            },
            Materials,
            cellsOfRecords: new Dictionary<RecordKey, ExteriorCell> { [EarlierRunsLand] = Cell });
        var quarter = new RestoredPartName.LandTextureQuarter(TerrainQuadrants.IndexOf(0, 0));

        var result = FindAfterEarlierRuns(plugin, [new RestoredPart(Later, EarlierRunsLand, quarter, Other)], WideSpot);

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.TerrainType)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>The cell's water an earlier run gave back from an untrusted mod counts as the world without that run's cleaned mod.</summary>
    [Fact]
    public void AWaterLevelThePatchWasGivenBackIsAcceptedWhileThePatchStillHoldsIt()
    {
        var water = new WaterCopies([new(Target, HasWater: true, -50f), new(Other, HasWater: true, 50f), new(Later, HasWater: true, -100f), new(Patch, HasWater: true, 50f)], []);
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] },
            water: new Dictionary<ExteriorCell, WaterCopies> { [Cell] = water },
            cellsOfRecords: new Dictionary<RecordKey, ExteriorCell> { [EarlierRunsCell] = Cell });
        var waterLevel = new RestoredPartName.Cell(new CellPartName(CellPart.WaterLevel, Region: null));

        Assert.NotEmpty(FindAfterEarlierRuns(plugin, []).Proposals);
        Assert.Empty(FindAfterEarlierRuns(plugin, [GivenBack(EarlierRunsCell, waterLevel, Other)]).Proposals);
    }

    private static ChangedTerrainPass FindAfterEarlierRuns(TerrainPlugin plugin, ImmutableArray<RestoredPart> restored) => FindAfterEarlierRuns(plugin, restored, Spot);

    private static ChangedTerrainPass FindAfterEarlierRuns(TerrainPlugin plugin, ImmutableArray<RestoredPart> restored, Vector3 position)
    {
        var world = TestScenes.CreateWorld([Placed(0, Rock, position)], []);
        return DecideAfterEarlierRuns(world, Mods.AcceptedWorld, plugin, restored, threads: 1, TestJudgedGroups.OfAll(world.Targets, Settings())).Result.Passes.Single();
    }

    private static readonly RecordKey EarlierRunsLand = new(BaseGame, 0x900);
    private static readonly RecordKey EarlierRunsCell = new(BaseGame, 0x901);

    private static RestoredPart GivenBack(RecordKey record, RestoredPartName part, PluginName source) => new(new PluginName("EarlierCleaned.esp"), record, part, source);

    private static CellTerrainComparison Comparison() =>
        new(new TerrainPlugins(Mods.AcceptedWorld, LoadOrder), new TerrainTypes(ChangedTerrainRules.ViewOfLoadOrder(new TerrainPlugin(new Dictionary<ExteriorCell, TerrainVersion[]>()), restored: [])), Settings());

    [Fact]
    public void TheLastLoadingChangingModIsNamedOverEveryCellTheAreaCrosses()
    {
        const int westCellEastEdge = 8 * TerrainGrid.VerticesPerSide + TerrainGrid.SquaresPerSide;
        const int eastCellWestEdgeNeighbour = 8 * TerrainGrid.VerticesPerSide + 1;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]>
        {
            [Cell] = [Flat(Target), RaisedAt(Later, westCellEastEdge)],
            [new ExteriorCell(Worldspace, 1, 0)] = [Flat(Target), RaisedAt(Other, eastCellWestEdgeNeighbour)],
        };

        var result = Find(terrain, Settings(), threads: 1, Placed(0, Rock, new Vector3(ExteriorGrid.CellSize, Spot.Y, 0)));

        Assert.Equal([Removal(0, Later)], result.Proposals.AsEnumerable());
        Assert.Equal(2, result.CellsCompared);
    }

    [Fact]
    public void TheBaseGameAsAMasterOfTheCleanedModIsAcceptedWhenTheCleanedModHasNoTerrainThere() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(BaseGame), RaisedAt(Other, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AModMatchingTheLastTargetMastersTerrainChangesNothingWhenTheTargetHasNoTerrainThere() =>
        Assert.Empty(FindInCell(Flat(BaseGame), RaisedAt(Master, Near), RaisedAt(Other, Near)).Proposals);

    [Fact]
    public void AModRestoringAnEarlierMastersTerrainOverTheLastTargetMastersChangesItWhenTheTargetHasNoTerrainThere() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(BaseGame), RaisedAt(Master, Near), Flat(Other)).Proposals.AsEnumerable());

    [Fact]
    public void AModThatIsNoTargetMasterLoadingBeforeTheTargetChangesTheMastersTerrain() =>
        Assert.Equal([Removal(0, Early)], FindInCell(Flat(BaseGame), RaisedAt(Early, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ADeletedLandRecordOfAnotherModIsAHeightChange() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), new TerrainVersion(Other, Heights: null, Layers: null)).Proposals.AsEnumerable());

    [Fact]
    public void ACellNoTrustedPluginHasALandRecordOfIsNotChecked()
    {
        var result = FindInCell(RaisedAt(Other, Near));

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 1), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// The other mod's land is the cell's first, as in a world it adds; OwnChanges.esp raises the vertex beside the object's, so only
    /// that vertex has an accepted height. The later mod changes the object's vertex or the one beside it.
    /// </summary>
    [Theory]
    [InlineData(2 * Raised, Raised, false)]
    [InlineData(Raised, Ground, true)]
    public void AVertexWithoutATrustedVersionIsNotChecked(float nearHeight, float besideHeight, bool removed)
    {
        var result = FindInCell(
            RaisedAt(Other, Near),
            OwnChangesRaisingBeside,
            new TerrainVersion(Later, Heights(Ground, (Near, nearHeight), (Beside, besideHeight)), Layers: null));

        ProposedRemoval[] expected = removed ? [Removal(0, Later)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// The other mod's flat land is the cell's first; OwnChanges.esp sinks the vertex beside the object's deep below it, so only that
    /// vertex is checked, and the later mod raises it a little. The object stands on the unchecked ground or hangs at the sunk vertex.
    /// </summary>
    [Theory]
    [InlineData(Ground, TerrainCheckedObjects.VisibleObjectsOnTheGround, false)]
    [InlineData(Ground, TerrainCheckedObjects.VisibleObjectsAtAnyHeight, true)]
    [InlineData(Deep, TerrainCheckedObjects.VisibleObjectsOnTheGround, true)]
    public void OnlyTheCheckedVerticesDecideWhetherAnObjectReachesTheGround(float objectHeight, TerrainCheckedObjects objects, bool removed)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]>
        {
            [Cell] = [Flat(Other), RaisedAt(OwnChanges, Beside, Deep), RaisedAt(Later, Beside, Deep + Raised)],
        };

        var result = Find(terrain, Settings(objects), threads: 1, Placed(0, Rock, Spot with { Z = objectHeight }));

        ProposedRemoval[] expected = removed ? [Removal(0, Later)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// OwnChanges.esp keeps the other mod's raise at the object's vertex and raises the vertex beside it. Over the target's flat land
    /// the object's vertex is accepted from the target and the one beside it from OwnChanges.esp, a terrain no single version holds.
    /// </summary>
    private static readonly TerrainVersion OwnChangesRaisingBeside = new(OwnChanges, Heights(Ground, (Near, Raised), (Beside, Raised)), Layers: null);

    [Fact]
    public void TheAcceptedHeightsTakeEachVertexFromItsOwnAcceptedVersion()
    {
        var terrain = Comparison().Compare([Flat(Target), RaisedAt(Other, Near), OwnChangesRaisingBeside], []);

        Assert.Equal((Ground, Raised), (terrain!.AcceptedHeights![Near], terrain.AcceptedHeights[Beside]));
    }

    /// <summary>The later mod keeps the accepted terrain, lowers the vertex beside the object's, or keeps the other mod's raise.</summary>
    [Theory]
    [InlineData(Ground, Raised, null)]
    [InlineData(Ground, Ground, "Later.esp")]
    [InlineData(Raised, Raised, "Other.esp")]
    public void EachVertexIsComparedWithItsOwnAcceptedVersion(float nearHeight, float besideHeight, string? changedBy)
    {
        var result = FindInCell(
            Flat(Target),
            RaisedAt(Other, Near),
            OwnChangesRaisingBeside,
            new TerrainVersion(Later, Heights(Ground, (Near, nearHeight), (Beside, besideHeight)), Layers: null));

        ProposedRemoval[] expected = changedBy is { } plugin ? [Removal(0, new PluginName(plugin))] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Fact]
    public void ObjectsInSpacesWithoutTerrainAreCountedAndNotChecked()
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };

        var result = Find(terrain, Settings(), threads: 1, Placed(0, Rock, space: Interior), Placed(1, Rock));

        Assert.Equal([Removal(1, Other)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.ObjectsWithoutTerrain);
    }

    [Theory]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, new[] { 0 })]
    [InlineData(TerrainCheckedObjects.VisibleObjectsAtAnyHeight, new[] { 0, 1 })]
    [InlineData(TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, new[] { 0, 1, 2 })]
    public void TheObjectsCheckedFollowTheSetting(TerrainCheckedObjects objects, int[] removed)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };
        TargetObject[] targets = [Placed(0, Rock), Placed(1, Rock, Spot + new Vector3(0, 0, 1000)), Placed(2, Marker)];

        var result = Find(terrain, Settings(objects), threads: 1, targets);

        Assert.Equal(removed, result.Proposals.Select(proposal => proposal.Target.Index));
    }

    [Theory]
    [InlineData(0.5f, false)]
    [InlineData(10f, true)]
    public void TheDistanceCanBeAMultipleOfTheObjectsSize(float multiplier, bool removed)
    {
        var aside = (int)(Spot.Y / TerrainGrid.VertexSpacing) * TerrainGrid.VerticesPerSide + 10;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, aside)] };

        var result = Find(terrain, Settings(bySize: true, multiplier: multiplier, byDistance: false));

        Assert.Equal(removed, result.Proposals.Any());
    }

    /// <summary>With the object 20 units wide, the 64-unit area reaches vertex column 9 and the 10-times-size area vertex column 10.</summary>
    [Theory]
    [InlineData(0.5f, 9, true)]
    [InlineData(10f, 10, true)]
    [InlineData(0.5f, 10, false)]
    [InlineData(10f, 11, false)]
    public void WithBothAreasOnAChangeInEitherAreaRemovesTheObject(float multiplier, int changedColumn, bool removed)
    {
        var changed = 8 * TerrainGrid.VerticesPerSide + changedColumn;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, changed)] };

        var result = Find(terrain, Settings(bySize: true, multiplier: multiplier, byDistance: true));

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnObjectWhoseCheckFailsIsReportedAndTheOthersAreStillChecked(int threads)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };
        TargetObject[] targets = [Placed(0, Rock), Placed(1, Broken), Placed(2, Rock)];

        var result = Find(terrain, Settings(), threads, targets);

        Assert.Equal([0, 2], result.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(
            new PartFailure(ChangedTerrainParts.TerrainCheck, "PlantedUserDataException: broken base", new FailureSubject(targets[1].Key.ToString(), null, 1)),
            Assert.Single(result.Failures));
    }

    [Fact]
    public void TheResultDoesNotDependOnTheThreadCount()
    {
        var terrain = Enumerable.Range(0, 4).ToDictionary(
            cellX => new ExteriorCell(Worldspace, cellX, 0),
            cellX => new[] { Flat(Target), RaisedAt(Other, Near), cellX % 2 == 0 ? RaisedAt(Later, Near, 2 * Raised) : RaisedAt(Patch, Near) });
        var targets = Enumerable.Range(0, 40)
            .Select(index => Placed(index, Rock, Spot + new Vector3(index % 4 * ExteriorGrid.CellSize, index / 4 * 4f, 0)))
            .ToArray();

        var single = Find(terrain, Settings(), threads: 1, targets);
        var parallel = Find(terrain, Settings(), threads: 4, targets);

        Assert.Equal(40, single.Proposals.Length);
        Assert.Equal(single.Proposals.AsEnumerable(), parallel.Proposals.AsEnumerable());
        Assert.Equal((4, 4), (single.CellsCompared, parallel.CellsCompared));
        Assert.Contains(Removal(1, Other), single.Proposals);
        Assert.Contains(Removal(0, Later), single.Proposals);
    }
}
