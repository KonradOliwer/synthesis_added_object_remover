using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// Which parts of another plugin's cell and LAND records go back to the version below the cleaned mod's, read from in-memory plugins
/// by the real reader and collector: the base game has a cell with its land, the cleaned mod overrides them and places an object of
/// its own in a cell of its own next to them, and a later plugin and the patch may override them again.
/// </summary>
public sealed class ChangedCellAndLandCollectingTests
{
    private const float HeightTolerance = 8f;
    private const float WaterHeightTolerance = 8f;
    private const float UnitsPerHeightStep = TerrainGrid.UnitsPerHeightStep;
    private const int Side = TerrainGrid.VerticesPerSide;
    private const int LastColumn = Side - 1;
    private const float BaseWaterHeight = -100f;
    private const float CleanedWaterHeight = 0f;

    private static readonly WhatToCollect Plan = new(new ScanNeeds(AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(HeightTolerance, WaterHeightTolerance), Cells: true, Lands: true)), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);
    private static readonly PluginName Patch = FixturePatcherState.PatchModKey.ToPluginName();
    private static readonly FormKey FirstRegion = FormKey.Factory("000C00:Base.esm");
    private static readonly FormKey SecondRegion = FormKey.Factory("000C01:Base.esm");

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FixtureMod _later;
    private readonly FormKey _worldspace;
    private readonly Cell _cell;
    private readonly Dictionary<ModKey, Worldspace> _worldspaceCopies = [];
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];

    public ChangedCellAndLandCollectingTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _worldspace = _base.NextKey();
        _cell = new Cell(_base.NextKey(), FixtureMod.Release)
        {
            Grid = new CellGrid { Point = new P2Int(0, 0) },
            Flags = Cell.Flag.HasWater,
            WaterHeight = BaseWaterHeight,
            Lighting = new CellLighting { FogNear = 10f },
            Landscape = FlatLand(_base.NextKey()),
        };
        WorldspaceIn(_base.Mod).AddCell(_cell);
        PlaceObjectOfTheCleanedMod();
    }

    private RecordKey CellKey => _cell.FormKey.ToRecordKey();

    private RecordKey LandKey => _cell.Landscape!.FormKey.ToRecordKey();

    private PluginName BaseGame => _base.Key.ToPluginName();

    private PluginName Cleaned => _cleaned.Key.ToPluginName();

    private PluginName Later => _later.Key.ToPluginName();

    public static TheoryData<CellPart> PartsChangedAlone() =>
        [.. Enum.GetValues<CellPart>().Where(part => part != CellPart.Region)];

    [Theory]
    [MemberData(nameof(PartsChangedAlone))]
    public void EachCellPartTheCleanedModChangesAloneGoesBack(CellPart part)
    {
        Change(CellCopyIn(_cleaned.Mod), part);

        var changed = ChangedCellOf(Collect());

        Assert.Equal([Part(part, new PartOutcome.Undo())], changed.Parts.ToList());
        Assert.Equal((_worldspace.ToRecordKey(), (0, 0), Cleaned, false), (changed.SpaceKey, changed.Grid!.Value, changed.WinningMod, changed.LinkedFromCleanedMod));
    }

    /// <summary>
    /// The later plugin carries the cleaned mod's music over. One that needs the cleaned mod keeps nothing, so the music goes back; one
    /// that loads without it was made against the base game's cell, so it set that music itself.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACellPartALaterPluginCarriesOverGoesBackOnlyWhenThatPluginNeedsTheCleanedMod(bool laterNeedsCleanedMod)
    {
        Change(CellCopyIn(_later.Mod, from: ChangedInCleanedMod(CellPart.Music)), CellPart.Music);

        Assert.Equal(
            [Part(CellPart.Music, laterNeedsCleanedMod ? new PartOutcome.Undo() : new PartOutcome.LeftToLaterPlugin(Later))],
            ChangedCellOf(Collect(laterNeedsCleanedMod: laterNeedsCleanedMod)).Parts.ToList());
    }

    [Fact]
    public void ACellPartThePatchChangedAgainIsLeftToThePatch()
    {
        var music = ChangedInCleanedMod(CellPart.Music);

        var changed = ChangedCellOf(Collect(patch => CellCopyIn(patch, from: music).Music.SetTo(OtherRecord(9))));

        Assert.Equal([Part(CellPart.Music, new PartOutcome.LeftToLaterPlugin(Patch))], changed.Parts.ToList());
    }

    /// <summary>The cleaned mod lists two more regions; an earlier patcher of the group takes the second out again.</summary>
    [Fact]
    public void ARegionThePatchChangesAgainIsLeftToItWhileTheOtherRegionGoesBack()
    {
        var withRegions = CellCopyIn(_cleaned.Mod);
        withRegions.Regions = [new FormLink<IRegionGetter>(FirstRegion), new FormLink<IRegionGetter>(SecondRegion)];

        Assert.Equal(
            [
                Part(CellPart.Region, new PartOutcome.Undo(), FirstRegion.ToRecordKey()),
                Part(CellPart.Region, new PartOutcome.LeftToLaterPlugin(Patch), SecondRegion.ToRecordKey()),
            ],
            ChangedCellOf(Collect(patch => CellCopyIn(patch, from: withRegions).Regions!.RemoveAt(1))).Parts.ToList());
    }

    /// <summary>
    /// The cleaned mod changes the music and the lock list (in the rest); an earlier patcher of the group carries both and sets another
    /// flag of the rest.
    /// </summary>
    [Fact]
    public void ALaterChangeInTheRestLeavesTheWholeRestToThatPluginWhileOtherPartsGoBack()
    {
        var changedInCleanedMod = ChangedInCleanedMod(CellPart.Music);
        Change(changedInCleanedMod, CellPart.RestOfCell);

        Assert.Equal(
            [Part(CellPart.Music, new PartOutcome.Undo()), Part(CellPart.RestOfCell, new PartOutcome.LeftToLaterPlugin(Patch))],
            ChangedCellOf(Collect(patch => CellCopyIn(patch, from: changedInCleanedMod).Flags |= (Cell.Flag)0x1000)).Parts.ToList());
    }

    [Fact]
    public void TheWaterValuesAreThoseOfTheVersionBelowTheCleanedModAndOfTheWinner()
    {
        Change(CellCopyIn(_later.Mod, from: ChangedInCleanedMod(CellPart.WaterLevel)), CellPart.Music);

        var changed = ChangedCellOf(Collect());

        Assert.Equal(new CellWaterCopy(BaseGame, true, BaseWaterHeight), changed.EarlierWater);
        Assert.Equal(new CellWaterCopy(Later, true, CleanedWaterHeight), changed.CurrentWater);
    }

    /// <summary>The base game's worldspace stores a default water height that the later plugin's copy of the worldspace changes.</summary>
    [Fact]
    public void TheDefaultWaterLevelIsTheWinningWorldspaces()
    {
        const float BaseDefault = -10f;
        const float LaterDefault = 25f;
        WorldspaceIn(_base.Mod).LandDefaults = new WorldspaceLandDefaults { DefaultWaterHeight = BaseDefault };
        WorldspaceIn(_later.Mod).LandDefaults = new WorldspaceLandDefaults { DefaultWaterHeight = LaterDefault };
        ChangedInCleanedMod(CellPart.WaterLevel);

        Assert.Equal(LaterDefault, ChangedCellOf(Collect()).DefaultWaterLevel);
    }

    /// <summary>The cleaned mod changes the music and the name; the later plugin carries the music, changes the name again and deletes the cell.</summary>
    [Fact]
    public void NothingGoesBackInACellALaterPluginDeletes()
    {
        var changedInCleanedMod = ChangedInCleanedMod(CellPart.Music);
        Change(changedInCleanedMod, CellPart.Name);
        var deleted = CellCopyIn(_later.Mod, from: changedInCleanedMod);
        deleted.Name = "Ruin";
        deleted.IsDeleted = true;

        var changed = ChangedCellOf(Collect());

        Assert.Contains(Part(CellPart.Name, new PartOutcome.LeftToLaterPlugin(Later)), changed.Parts);
        Assert.All(changed.Parts, part => Assert.IsType<PartOutcome.LeftToLaterPlugin>(part.Outcome));
    }

    [Fact]
    public void ACellTheCleanedModDeletesGivesNoChangedCell()
    {
        ChangedInCleanedMod(CellPart.Music).IsDeleted = true;

        Assert.DoesNotContain(Collect().ChangedCells, changed => changed.Cell == CellKey);
    }

    [Fact]
    public void AnInteriorIsItsOwnSpaceAndHasNoGrid()
    {
        var interior = new Cell(_base.NextKey(), FixtureMod.Release) { Flags = Cell.Flag.IsInteriorCell };
        _base.Mod.Cells.AddInteriorCell(interior);
        var copy = (Cell)interior.DeepCopy();
        copy.Music.SetTo(OtherRecord(3));
        copy.Temporary.Add(PlacedObjectOfTheCleanedMod("InteriorStatic"));
        _cleaned.Mod.Cells.AddInteriorCell(copy);

        var changed = Assert.Single(Collect().ChangedCells, cell => cell.Cell == interior.FormKey.ToRecordKey());

        Assert.Equal((interior.FormKey.ToRecordKey(), (ValueTuple<int, int>?)null), (changed.SpaceKey, changed.Grid));
        Assert.Equal([Part(CellPart.Music, new PartOutcome.Undo())], changed.Parts.ToList());
    }

    /// <summary>The cleaned mod changes a cell of another worldspace, where it places nothing.</summary>
    [Fact]
    public void ACellOutsideTheTargetSpacesIsNotRead()
    {
        var elsewhere = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var otherWorldspace = new Worldspace(_base.NextKey(), FixtureMod.Release);
        otherWorldspace.AddCell(elsewhere);
        _base.Mod.Worldspaces.Add(otherWorldspace);
        var copy = (Cell)elsewhere.DeepCopy();
        copy.Music.SetTo(OtherRecord(3));
        var otherWorldspaceCopy = new Worldspace(otherWorldspace.FormKey, FixtureMod.Release);
        otherWorldspaceCopy.AddCell(copy);
        _cleaned.Mod.Worldspaces.Add(otherWorldspaceCopy);

        Assert.DoesNotContain(Collect().ChangedCells, changed => changed.Cell == elsewhere.FormKey.ToRecordKey());
    }

    [Fact]
    public void ACellARecordOfTheCleanedModLinksToIsMarked()
    {
        ChangedInCleanedMod(CellPart.Music);
        _cleaned.AddFormList("CellList").Items.Add(new FormLink<ISkyrimMajorRecordGetter>(_cell.FormKey));

        Assert.True(ChangedCellOf(Collect()).LinkedFromCleanedMod);
    }

    [Fact]
    public void WithoutTheRestoreGroupNoCellOrLandIsJudged()
    {
        ChangedInCleanedMod(CellPart.Music);
        RaisePoint(LandCopyIn(_cleaned.Mod), LastColumn, 10, steps: 2);

        var collected = Collect(plan: Plan with { Needs = Plan.Needs with { AreaRecordVersions = null } });

        Assert.Empty(collected.ChangedCells);
        Assert.Empty(collected.ChangedLands);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OnlyTheCellsAndTheLandsAskedForAreRead(bool cells, bool lands)
    {
        ChangedInCleanedMod(CellPart.Music);
        RaisePoint(LandCopyIn(_cleaned.Mod), LastColumn, 10, steps: 2);

        var collected = Collect(plan: Plan with { Needs = Plan.Needs with { AreaRecordVersions = Plan.Needs.AreaRecordVersions! with { Cells = cells, Lands = lands } } });

        Assert.Equal((cells, lands), (collected.ChangedCells.Length == 1, collected.ChangedLands.Length == 1));
    }

    /// <summary>The cleaned mod raises a point of the land and copies the cell unchanged.</summary>
    [Fact]
    public void TheHandlesKeepOnlyTheCellsAndLandsWithAChange()
    {
        RaisePoint(LandCopyIn(_cleaned.Mod), LastColumn, 10, steps: 2);
        RecordHandles? handles = null;

        var collected = Collect(afterCollect: records => handles = (RecordHandles)records.ReadWritableRecords());

        Assert.Empty(collected.ChangedCells);
        Assert.Equal([BaseGame, Cleaned], handles!.LandOf(LandKey).Plugins.ToList());
        Assert.Throws<KeyNotFoundException>(() => handles.CellOf(CellKey));
    }

    /// <summary>The cleaned mod raises a point; a later plugin's copy of the cell holding the LAND record deletes the cell, or does not.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALandRecordSaysWhetherAVersionDeletesItsCell(bool deletedLater)
    {
        RaisePoint(LandCopyIn(_cleaned.Mod), LastColumn, 10, steps: 2);
        CellCopyIn(_later.Mod).IsDeleted = deletedLater;

        Assert.Equal(deletedLater, ChangedLandOf(Collect()).CellDeletedInAVersion);
    }

    /// <summary>
    /// The cleaned mod raises one point beyond the tolerance, one within it and a third that a later plugin that needs the cleaned mod
    /// carries over and an earlier patcher of the group raises again; the patch carries the rest.
    /// </summary>
    [Fact]
    public void EachPointRaisedBeyondTheToleranceGoesBackUnlessThePatchRaisedItAgain()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        RaisePoint(raised, LastColumn, 11, steps: 1);
        RaisePoint(raised, LastColumn, 12, steps: 2);
        var carried = LandCopyIn(_later.Mod, from: raised);

        var land = ChangedLandOf(Collect(patch => RaisePoint(LandCopyIn(patch, from: carried), LastColumn, 12, steps: 4), laterNeedsCleanedMod: true));

        Assert.Equal([PointIndex(LastColumn, 10)], land.UndoPoints.Points);
        Assert.Equal([PointIndex(LastColumn, 12)], land.LeftToLater.Points.Points);
        Assert.Equal([Patch], land.LeftToLater.Plugins.ToList());
        Assert.Equal((0f, 2 * UnitsPerHeightStep), (land.Heights!.Earlier![PointIndex(LastColumn, 10)], land.Heights.Current![PointIndex(LastColumn, 10)]));
        Assert.Equal((new ExteriorCell(_worldspace.ToRecordKey(), 0, 0), Patch), (land.Cell, land.WinningMod));
        Assert.True(land.UndoColours.IsEmpty && land.UndoQuarters.IsEmpty && !land.UndoRest);
    }

    /// <summary>
    /// The cleaned mod raises a point; a later plugin that loads without it holds that height. It was made against the base game's
    /// land, so it set the height itself, and the point is left to it.
    /// </summary>
    [Fact]
    public void APointALaterPluginThatLoadsWithoutTheCleanedModHoldsIsLeftToThatPlugin()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        LandCopyIn(_later.Mod, from: raised);

        var land = ChangedLandOf(Collect());

        Assert.True(land.UndoPoints.IsEmpty);
        Assert.Equal([PointIndex(LastColumn, 10)], land.LeftToLater.Points.Points);
        Assert.Equal([Later], land.LeftToLater.Plugins.ToList());
    }

    [Fact]
    public void ATurnedNormalGoesBackWithItsPoint()
    {
        LandCopyIn(_cleaned.Mod).VertexNormals![5, 6] = new P3UInt8(1, 2, 3);

        var land = ChangedLandOf(Collect());

        Assert.Equal([PointIndex(5, 6)], land.UndoPoints.Points);
    }

    [Fact]
    public void ARecolouredPointGoesBackWithItsColourOnly()
    {
        LandCopyIn(_cleaned.Mod).VertexColors![5, 6] = new P3UInt8(9, 8, 7);

        var land = ChangedLandOf(Collect());

        Assert.Equal([PointIndex(5, 6)], land.UndoColours.Points);
        Assert.True(land.UndoPoints.IsEmpty);
    }

    /// <summary>The cleaned mod recolours a point and paints a quarter, but moves no point; the later plugin raises a point.</summary>
    [Fact]
    public void ALandWithoutAPointWhoseHeightOrNormalTheCleanedModChangedKeepsItsHeights()
    {
        var painted = LandCopyIn(_cleaned.Mod);
        painted.VertexColors![5, 6] = new P3UInt8(9, 8, 7);
        painted.Layers.Add(AlphaLayer(Quadrant.TopRight, layerNumber: 1));
        RaisePoint(LandCopyIn(_later.Mod, from: painted), LastColumn, 10, steps: 2);

        var heights = ChangedLandOf(Collect()).Heights!;

        Assert.Equal((0f, 2 * UnitsPerHeightStep), (heights.Earlier![PointIndex(LastColumn, 10)], heights.Current![PointIndex(LastColumn, 10)]));
    }

    /// <summary>The cleaned mod recolours a point and paints a quarter; no version moves a point, so the heights are read again where needed.</summary>
    [Fact]
    public void ALandWhoseHeightsAndNormalsNoVersionChangedKeepsNoHeights()
    {
        var painted = LandCopyIn(_cleaned.Mod);
        painted.VertexColors![5, 6] = new P3UInt8(9, 8, 7);
        painted.Layers.Add(AlphaLayer(Quadrant.TopRight, layerNumber: 1));

        Assert.Null(ChangedLandOf(Collect()).Heights);
    }

    /// <summary>The cleaned mod paints the top right and the bottom left quarters; an earlier patcher of the group carries both and repaints the bottom left one.</summary>
    [Fact]
    public void AQuarterTheCleanedModPaintedGoesBackUnlessThePatchRepaintedIt()
    {
        var painted = LandCopyIn(_cleaned.Mod);
        painted.Layers.Add(AlphaLayer(Quadrant.TopRight, layerNumber: 1));
        painted.Layers.Add(AlphaLayer(Quadrant.BottomLeft, layerNumber: 1));

        var land = ChangedLandOf(Collect(patch =>
        {
            var repainted = LandCopyIn(patch, from: painted);
            repainted.Layers.RemoveAll(layer => layer.Header!.Quadrant == Quadrant.BottomLeft);
            repainted.Layers.Add(AlphaLayer(Quadrant.BottomLeft, layerNumber: 2));
        }));

        Assert.Equal([TerrainQuadrants.IndexOf(1, 1)], land.UndoQuarters.Quarters);
        Assert.Equal([TerrainQuadrants.IndexOf(0, 0)], land.LeftToLater.Quarters.Quarters);
        Assert.True(land.UndoPoints.IsEmpty && !land.UndoRest);
    }

    /// <summary>The later plugin, a patch for the cleaned mod, carries the raised point and deletes the land, so no part is left to it either.</summary>
    [Fact]
    public void ALandALaterPluginDeletesGivesNoChangedLand()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        LandCopyIn(_later.Mod, from: raised).IsDeleted = true;

        Assert.DoesNotContain(Collect(laterNeedsCleanedMod: true).ChangedLands, land => land.LandKey == LandKey);
    }

    /// <summary>
    /// The cleaned mod raises two points; the later plugin, which loads without it, holds the first, raises the second again and
    /// deletes the land: both points are its own.
    /// </summary>
    [Fact]
    public void NothingGoesBackInALandALaterPluginDeletesWhileItsOwnChangesAreLeftToIt()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        RaisePoint(raised, LastColumn, 12, steps: 2);
        var deleted = LandCopyIn(_later.Mod, from: raised);
        deleted.VertexNormals![LastColumn, 12] = new P3UInt8(1, 2, 3);
        deleted.IsDeleted = true;

        var land = ChangedLandOf(Collect());

        Assert.True(land.UndoPoints.IsEmpty && land.UndoColours.IsEmpty && land.UndoQuarters.IsEmpty && !land.UndoRest);
        Assert.Equal([PointIndex(LastColumn, 10), PointIndex(LastColumn, 12)], land.LeftToLater.Points.Points);
    }

    [Fact]
    public void TheHandlesHoldEachVersionFromBelowTheCleanedModUpToTheWinnerThePatchIncluded()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        Change(_cellCopies[_cleaned.Key], CellPart.Music);
        var carried = LandCopyIn(_later.Mod, from: raised);
        RecordHandles? handles = null;

        Collect(patch => CellCopyIn(patch, from: _cellCopies[_later.Key]).Landscape = (Landscape)carried.DeepCopy(), records => handles = (RecordHandles)records.ReadWritableRecords());

        Assert.Equal([BaseGame, Cleaned, Later, Patch], handles!.CellOf(CellKey).Plugins.ToList());
        Assert.Equal([BaseGame, Cleaned, Later, Patch], handles.LandOf(LandKey).Plugins.ToList());
        var point = PointIndex(LastColumn, 10);
        Assert.Equal((0f, 2 * UnitsPerHeightStep), (LandHeightReader.Read(handles.LandOf(LandKey).Earlier)![point], LandHeightReader.Read(handles.LandOf(LandKey).Winner)![point]));
    }

    /// <summary>
    /// The cleaned mod changes the music and the location; the later plugin, made against the base game's cell, keeps the base game's
    /// music and changes the location again.
    /// </summary>
    [Fact]
    public void ARestoredCellGetsBackOnlyItsUndonePartsWhileThePartLeftToALaterPluginKeepsItsValue()
    {
        var changed = ChangedInCleanedMod(CellPart.Music);
        Change(changed, CellPart.Location);
        CellCopyIn(_later.Mod).Location.SetTo(OtherRecord(11));

        var patch = RestoreTheChangedCell();

        var written = Assert.Single(patch.EnumerateMajorRecords<ICellGetter>(), cell => cell.FormKey == _cell.FormKey);
        Assert.Equal((_cell.Music.FormKey, OtherRecord(11)), (written.Music.FormKey, written.Location.FormKey));
        Assert.Equal((_cell.Lighting!.FogNear, _cell.WaterHeight), (written.Lighting!.FogNear, written.WaterHeight));
    }

    /// <summary>
    /// The cleaned mod raises two inner points and recolours one; the later plugin, which needs the cleaned mod, carries them and raises
    /// the second point again. Its changes keep nothing, so the base game's heights and colour are written back. The whole cell is cleared.
    /// </summary>
    [Fact]
    public void ARestoredLandGetsItsEarlierHeightsAndColourBackWhereAPluginThatNeedsTheCleanedModCarriedOrRaisedThem()
    {
        var (restored, raisedAgain) = (PointIndex(10, 10), PointIndex(20, 10));
        var raised = LandCopyIn(_cleaned.Mod);
        RaiseInnerPoint(raised, 10, 10, steps: 2);
        RaiseInnerPoint(raised, 20, 10, steps: 2);
        raised.VertexColors![5, 6] = new P3UInt8(9, 8, 7);
        RaiseInnerPoint(LandCopyIn(_later.Mod, from: raised), 20, 10, steps: 4);

        var patch = RestoreInPatch(
            collected =>
            {
                var cleared = TestAreaShares.Quarter(AreaVerdict.Cleared);
                var land = new LandAreasJudged(ChangedLandOf(collected), Never: null, [cleared, cleared, cleared, cleared]);
                var judged = new TerrainUndoPlan([land], _ => new LandHeights(null, null), HeightTolerance, blendDistance: 0f).Judged();
                return new RestoreWhereRemovedResult(StepOutcome.Performed, [], [], judged, [], [], Passes: 1, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
            },
            laterNeedsCleanedMod: true);

        var written = Assert.Single(patch.EnumerateMajorRecords<ILandscapeGetter>());
        var heights = LandHeightReader.Read(written)!;
        Assert.Equal((0f, 0f), (heights[restored], heights[raisedAgain]));
        Assert.Equal(new P3UInt8(0, 0, 0), written.VertexColors![5, 6]);
    }

    [Fact]
    public void TheFactsDoNotDependOnTheThreadCount()
    {
        var raised = LandCopyIn(_cleaned.Mod);
        RaisePoint(raised, LastColumn, 10, steps: 2);
        raised.VertexColors![5, 6] = new P3UInt8(9, 8, 7);
        Change(_cellCopies[_cleaned.Key], CellPart.Music);
        Change(_cellCopies[_cleaned.Key], CellPart.Name);

        Assert.Equal(Describe(Collect(threads: 1)), Describe(Collect(threads: 8)));
    }

    private static string Describe(CollectedObjects collected) => string.Join(
        Environment.NewLine,
        collected.ChangedCells.Select(cell => $"{cell.Cell} {string.Join(", ", cell.Parts)}")
            .Concat(collected.ChangedLands.Select(land =>
                $"{land.LandKey} {string.Join(",", land.UndoPoints.Points)} {string.Join(",", land.UndoColours.Points)} {land.UndoQuarters} {land.UndoRest} "
                + $"{string.Join(",", land.Heights!.Earlier!)} {string.Join(",", land.Heights.Current!)}")));

    private static int PointIndex(int x, int y) => y * Side + x;

    private static PartChange Part(CellPart part, PartOutcome outcome, RecordKey? region = null) => new(new CellPartName(part, region), outcome);

    /// <remarks>Heights are stored as steps from the point before in the row, so a step at the row's last point raises only that point.</remarks>
    private static void RaisePoint(Landscape land, int x, int y, sbyte steps) => land.VertexHeightMap!.HeightMap[x, y] = steps;

    /// <summary>Raises one point inside a row: its step from the point before goes up and the next point's step comes down by as much.</summary>
    private static void RaiseInnerPoint(Landscape land, int x, int y, sbyte steps)
    {
        land.VertexHeightMap!.HeightMap[x, y] = steps;
        land.VertexHeightMap.HeightMap[x + 1, y] = (sbyte)-steps;
    }

    private static FormKey OtherRecord(int index) => FormKey.Factory($"000D{index:X2}:Base.esm");

    private static void Change(Cell cell, CellPart part)
    {
        switch (part)
        {
            case CellPart.Lighting: cell.Lighting!.FogNear = 20f; break;
            case CellPart.ImageSpace: cell.ImageSpace.SetTo(OtherRecord(1)); break;
            case CellPart.AcousticSpace: cell.AcousticSpace.SetTo(OtherRecord(2)); break;
            case CellPart.Music: cell.Music.SetTo(OtherRecord(3)); break;
            case CellPart.WaterLevel: cell.WaterHeight = CleanedWaterHeight; break;
            case CellPart.WaterLook: cell.Water.SetTo(OtherRecord(4)); break;
            case CellPart.SkyAndWeatherFromRegion: cell.SkyAndWeatherFromRegion.SetTo(OtherRecord(5)); break;
            case CellPart.Name: cell.Name = "Village"; break;
            case CellPart.Ownership: cell.Owner.SetTo(OtherRecord(6)); break;
            case CellPart.EncounterZone: cell.EncounterZone.SetTo(OtherRecord(7)); break;
            case CellPart.Location: cell.Location.SetTo(OtherRecord(8)); break;
            case CellPart.CantTravelFromHere: cell.Flags |= Cell.Flag.CantTravelFromHere; break;
            case CellPart.NoLodWater: cell.Flags |= Cell.Flag.NoLodWater; break;
            case CellPart.PublicArea: cell.Flags |= Cell.Flag.PublicArea; break;
            case CellPart.HandChanged: cell.Flags |= Cell.Flag.HandChanged; break;
            case CellPart.ShowSky: cell.Flags |= Cell.Flag.ShowSky; break;
            case CellPart.UseSkyLighting: cell.Flags |= Cell.Flag.UseSkyLighting; break;
            case CellPart.OffLimits: cell.MajorFlags |= Cell.MajorFlag.OffLimits; break;
            case CellPart.CantWait: cell.MajorFlags |= Cell.MajorFlag.CantWait; break;
            case CellPart.RestOfCell: cell.LockList.SetTo(OtherRecord(10)); break;
            default: throw new ArgumentOutOfRangeException(nameof(part), part, null);
        }
    }

    private Cell ChangedInCleanedMod(CellPart part)
    {
        var copy = CellCopyIn(_cleaned.Mod);
        Change(copy, part);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's cell, without the land, made from the given version (the base game's by default) on first use.</summary>
    private Cell CellCopyIn(ISkyrimMod mod, Cell? from = null)
    {
        if (_cellCopies.TryGetValue(mod.ModKey, out var copy)) return copy;
        copy = (Cell)(from ?? _cell).DeepCopy();
        copy.Landscape = null;
        WorldspaceIn(mod).AddCell(copy);
        _cellCopies.Add(mod.ModKey, copy);
        return copy;
    }

    /// <summary>The plugin's copy of the base game's land, in its copy of the cell, made from the given version (the base game's by default) on first use.</summary>
    private Landscape LandCopyIn(ISkyrimMod mod, Landscape? from = null)
    {
        var cell = CellCopyIn(mod);
        return cell.Landscape ??= (Landscape)(from ?? _cell.Landscape!).DeepCopy();
    }

    private Worldspace WorldspaceIn(ISkyrimMod mod)
    {
        if (_worldspaceCopies.TryGetValue(mod.ModKey, out var worldspace)) return worldspace;
        worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        mod.Worldspaces.Add(worldspace);
        _worldspaceCopies.Add(mod.ModKey, worldspace);
        return worldspace;
    }

    /// <summary>The cleaned mod's own object, in a cell of its own next to the base game's cell, which makes the worldspace a target space.</summary>
    private void PlaceObjectOfTheCleanedMod()
    {
        var ownCell = new Cell(_cleaned.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(1, 0) } };
        ownCell.Temporary.Add(PlacedObjectOfTheCleanedMod("OwnStatic"));
        WorldspaceIn(_cleaned.Mod).AddCell(ownCell);
    }

    private PlacedObject PlacedObjectOfTheCleanedMod(string baseEditorId)
    {
        var placed = new PlacedObject(_cleaned.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = new P3Float(5000, 100, 0) } };
        placed.Base.SetTo(_cleaned.AddMarkerStatic(baseEditorId));
        return placed;
    }

    private static Landscape FlatLand(FormKey key) => new(key, FixtureMod.Release)
    {
        VertexHeightMap = new LandscapeVertexHeightMap { Offset = 0f, HeightMap = new Array2d<sbyte>(new P2Int(Side, Side), default) },
        VertexNormals = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
        VertexColors = new Array2d<P3UInt8>(new P2Int(Side, Side), default),
    };

    private static AlphaLayer AlphaLayer(Quadrant quadrant, ushort layerNumber) => new()
    {
        Header = new LayerHeader { Quadrant = quadrant, Texture = new FormLink<ILandscapeTextureGetter>(FormKey.Factory("000F01:Base.esm")), LayerNumber = layerNumber },
    };

    /// <param name="writeIntoPatch">What an earlier patcher of the group wrote into the patch; nothing when null.</param>
    /// <param name="afterCollect">Reads the plugin records the collector read, before they are dropped.</param>
    /// <param name="laterNeedsCleanedMod">The later plugin cannot load without the cleaned mod, as a patch for it.</param>
    private CollectedObjects Collect(
        Action<ISkyrimMod>? writeIntoPatch = null, Action<IPluginRecords>? afterCollect = null, WhatToCollect? plan = null, int threads = 1, bool laterNeedsCleanedMod = false)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        writeIntoPatch?.Invoke(state.PatchMod);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, threads).Open();
        var collected = PlacedObjects.Collect(records, TrustOf(laterNeedsCleanedMod), plan ?? Plan);
        afterCollect?.Invoke(records);
        return collected;
    }

    /// <param name="laterNeedsCleanedMod">The later plugin cannot load without the cleaned mod, as a patch for it.</param>
    private ModTrustTable TrustOf(bool laterNeedsCleanedMod) =>
        laterNeedsCleanedMod
            ? TestModTrust.Of(Cleaned, Patch, trusted: [BaseGame], untrusted: [], needingCleanedMod: [Later])
            : TestModTrust.Of(Cleaned, Patch, trusted: [BaseGame, Later], untrusted: []);

    /// <summary>Collects, judges the base game's cell as lying in a cleared area and writes the patch.</summary>
    private ISkyrimModGetter RestoreTheChangedCell() => RestoreInPatch(collected =>
    {
        var changed = ChangedCellOf(collected);
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [],
            [new CellChangeJudged(changed, [.. changed.Parts.Where(part => part.Outcome is PartOutcome.Undo)], null, cleared, WaterKeptBy: null, IsRestored: true, KeptForArea: null)],
            [], [], [], Passes: 1, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
    });

    /// <summary>Collects, judges what goes back with the given judging and writes the patch.</summary>
    /// <param name="laterNeedsCleanedMod">The later plugin cannot load without the cleaned mod, as a patch for it.</param>
    private ISkyrimModGetter RestoreInPatch(Func<CollectedObjects, RestoreWhereRemovedResult> judge, bool laterNeedsCleanedMod = false)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, 1).Open();
        var collected = PlacedObjects.Collect(records, TrustOf(laterNeedsCleanedMod), Plan);
        var protection = ObjectsToKeep.Build(collected.Targets, [], TestTargets.References(collected.Targets.Length), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [.. collected.Targets.Select(_ => (WhenRemoved)new WhenRemoved.Disable())]);
        PatchWriter.Write(new(final, collected.Targets, OtherModsObjectsResult.SwitchedOff, judge(collected), records, new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText)));
        return state.PatchMod;
    }

    private CellChangedByCleanedMod ChangedCellOf(CollectedObjects collected) => Assert.Single(collected.ChangedCells, changed => changed.Cell == CellKey);

    private LandChangedByCleanedMod ChangedLandOf(CollectedObjects collected) => Assert.Single(collected.ChangedLands, changed => changed.LandKey == LandKey);
}
