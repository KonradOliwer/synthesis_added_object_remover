using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// The reader keeps each plugin's own land record of a cell, in load order, only when the scope asks for the versions, and each
/// plugin's own water fields of the cell only when it asks for the water.
/// </summary>
public class TerrainVersionReadingTests
{
    private const float BaseOffset = 10f;
    private const float TargetOffset = 12f;
    private const float LateOffset = 20f;
    private const float UnitsPerHeightStep = 8f;
    private const float SecondCellWater = -30f;

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _target;
    private readonly FixtureMod _noLand;
    private readonly FixtureMod _late;
    private readonly FormKey _worldspace;
    private readonly FormKey _cell;

    public TerrainVersionReadingTests()
    {
        _target = new FixtureMod("Target.esp", _base);
        _noLand = new FixtureMod("NoLand.esp", _base);
        _late = new FixtureMod("Late.esp", _base);
        _worldspace = _base.NextKey();
        _cell = _base.NextKey();
        AddCell(_base, BaseOffset);
        var targetCell = AddCell(_target, TargetOffset);
        AddCell(_noLand, landOffset: null);
        AddCell(_late, LateOffset);

        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release);
        placed.Base.SetTo(_base.AddMarkerStatic("BaseStatic"));
        targetCell.Temporary.Add(placed);
    }

    private ExteriorCell ExteriorCell => new(_worldspace.ToRecordKey(), 0, 0);

    [Fact]
    public void VersionsHoldEveryPluginsOwnHeightsInLoadOrder()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        var versions = plugin.ReadTerrainVersions(ExteriorCell);

        Assert.Equal(
            [_base.Key.ToPluginName(), _target.Key.ToPluginName(), _late.Key.ToPluginName()],
            versions.Select(version => version.Plugin));
        Assert.Equal(
            [BaseOffset * UnitsPerHeightStep, TargetOffset * UnitsPerHeightStep, LateOffset * UnitsPerHeightStep],
            versions.Select(version => version.Heights![0]));
    }

    [Fact]
    public void WinningHeightsAreStillTheLastVersions()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: true, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        Assert.Equal(LateOffset * UnitsPerHeightStep, plugin.ReadTerrain(ExteriorCell)![0]);
    }

    [Fact]
    public void VersionsAreNotKeptUnlessAskedFor()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: true, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        Assert.Throws<InvalidOperationException>(() => plugin.ReadTerrainVersions(ExteriorCell));
    }

    [Fact]
    public void AChildWorldspaceUsingItsParentsLandIsReadOnTheParentsVersions()
    {
        var child = _base.NextKey();
        AddChildWorldspace(_base, child, cell: null);
        var childCell = new Cell(_target.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release);
        placed.Base.SetTo(_base.AddMarkerStatic("ChildStatic"));
        childCell.Temporary.Add(placed);
        AddChildWorldspace(_target, child, childCell);
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        Assert.Equal(_worldspace.ToRecordKey(), plugin.FindLandWorldspace(child.ToRecordKey()));
        Assert.Equal(
            [_base.Key.ToPluginName(), _target.Key.ToPluginName(), _late.Key.ToPluginName()],
            plugin.ReadTerrainVersions(ExteriorCell).Select(version => version.Plugin));
    }

    /// <summary>The late plugin's second cell at the grid is found after its first, so the first, which the game shows, is last.</summary>
    [Fact]
    public void TwoCellsAtOneGridInOnePluginGiveTwoWaterCopiesWithTheFirstFoundLast()
    {
        var secondCell = new Cell(_late.NextKey(), FixtureMod.Release)
        {
            Grid = new CellGrid { Point = new P2Int(0, 0) }, Flags = Cell.Flag.HasWater, WaterHeight = SecondCellWater,
        };
        _late.Mod.Worldspaces.RecordCache[_worldspace].AddCell(secondCell);
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: true, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        var late = _late.Key.ToPluginName();
        Assert.Equal(
            [
                new CellWaterCopy(_base.Key.ToPluginName(), false, null), new CellWaterCopy(_target.Key.ToPluginName(), false, null),
                new CellWaterCopy(_noLand.Key.ToPluginName(), false, null), new CellWaterCopy(late, true, SecondCellWater),
                new CellWaterCopy(late, false, null),
            ],
            plugin.ReadWaterCopies(ExteriorCell).Cell);
    }

    [Fact]
    public void WaterIsNotKeptUnlessAskedFor()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);

        Assert.Throws<InvalidOperationException>(() => plugin.ReadWaterCopies(ExteriorCell));
    }

    private const float ParentBaseWater = 5f;
    private const float ParentLateWater = 9f;
    private const float ChildBaseWater = 7f;

    [Fact]
    public void AChildWorldspaceUsingItsParentsWaterDataReadsTheParentsDefaultsAndItsOwnCells()
    {
        SetDefaultWater(_base, _worldspace, ParentBaseWater);
        SetDefaultWater(_late, _worldspace, ParentLateWater);
        var child = AddChildWithTargetCell(WorldspaceParent.Flag.UseLandData | WorldspaceParent.Flag.UseWaterData, childDefaultWater: ChildBaseWater);
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(WaterScope(), TestVersionWalks.NoStop);

        var copies = plugin.ReadWaterCopies(new ExteriorCell(child.ToRecordKey(), 0, 0));

        Assert.Equal(
            [
                new WorldspaceWaterCopy(_base.Key.ToPluginName(), ParentBaseWater), new WorldspaceWaterCopy(_target.Key.ToPluginName(), null),
                new WorldspaceWaterCopy(_noLand.Key.ToPluginName(), null), new WorldspaceWaterCopy(_late.Key.ToPluginName(), ParentLateWater),
            ],
            copies.Worldspace);
        Assert.Equal([new CellWaterCopy(_target.Key.ToPluginName(), false, null)], copies.Cell);
    }

    [Fact]
    public void AChildWorldspaceNotUsingItsParentsWaterDataReadsItsOwnDefaults()
    {
        SetDefaultWater(_base, _worldspace, ParentBaseWater);
        var child = AddChildWithTargetCell(WorldspaceParent.Flag.UseLandData, childDefaultWater: ChildBaseWater);
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(WaterScope(), TestVersionWalks.NoStop);

        Assert.Equal(
            [new WorldspaceWaterCopy(_base.Key.ToPluginName(), ChildBaseWater), new WorldspaceWaterCopy(_target.Key.ToPluginName(), null)],
            plugin.ReadWaterCopies(new ExteriorCell(child.ToRecordKey(), 0, 0)).Worldspace);
    }

    [Fact]
    public void AWorldspaceCopyWithoutLandDefaultsHasNoDefaultLevel()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(WaterScope(), TestVersionWalks.NoStop);

        Assert.All(plugin.ReadWaterCopies(ExteriorCell).Worldspace, copy => Assert.Null(copy.DefaultLevel));
    }

    [Fact]
    public void AWorldspaceParentLoopStopsAtTheLastWorldspaceNotYetSeen()
    {
        var second = _base.NextKey();
        var first = AddChildWithTargetCell(WorldspaceParent.Flag.UseWaterData, childDefaultWater: ChildBaseWater, parent: second);
        AddChildWorldspace(_base, second, cell: null, WorldspaceParent.Flag.UseWaterData, ParentBaseWater, parent: first);
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(WaterScope(), TestVersionWalks.NoStop);

        Assert.Equal(
            [new WorldspaceWaterCopy(_base.Key.ToPluginName(), ParentBaseWater)],
            plugin.ReadWaterCopies(new ExteriorCell(first.ToRecordKey(), 0, 0)).Worldspace);
    }

    [Fact]
    public void AWorldspaceWhoseParentDoesNotResolveReadsItsOwnDefaults()
    {
        var child = AddChildWithTargetCell(WorldspaceParent.Flag.UseWaterData, childDefaultWater: ChildBaseWater, parent: _base.NextKey());
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(WaterScope(), TestVersionWalks.NoStop);

        Assert.Equal(
            [new WorldspaceWaterCopy(_base.Key.ToPluginName(), ChildBaseWater), new WorldspaceWaterCopy(_target.Key.ToPluginName(), null)],
            plugin.ReadWaterCopies(new ExteriorCell(child.ToRecordKey(), 0, 0)).Worldspace);
    }

    private PlacedReadScope WaterScope() =>
        new(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, Water: true, TestRecordKeys.HoldKeyword);

    private static void SetDefaultWater(FixtureMod mod, FormKey worldspace, float height) =>
        mod.Mod.Worldspaces.RecordCache[worldspace].LandDefaults = new WorldspaceLandDefaults { DefaultWaterHeight = height };

    /// <summary>A child worldspace of the given parent (the test worldspace by default), with the target's cell at the grid holding a target object.</summary>
    private FormKey AddChildWithTargetCell(WorldspaceParent.Flag flags, float childDefaultWater, FormKey? parent = null)
    {
        var child = _base.NextKey();
        AddChildWorldspace(_base, child, cell: null, flags, childDefaultWater, parent);
        var childCell = new Cell(_target.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release);
        placed.Base.SetTo(_base.AddMarkerStatic("ChildWaterStatic"));
        childCell.Temporary.Add(placed);
        AddChildWorldspace(_target, child, childCell, flags, parent: parent);
        return child;
    }

    private void AddChildWorldspace(
        FixtureMod mod,
        FormKey child,
        Cell? cell,
        WorldspaceParent.Flag flags = WorldspaceParent.Flag.UseLandData,
        float? childDefaultWater = null,
        FormKey? parent = null)
    {
        var worldspace = new Worldspace(child, FixtureMod.Release)
        {
            Parent = new WorldspaceParent { Worldspace = new FormLink<IWorldspaceGetter>(parent ?? _worldspace), Flags = flags },
            LandDefaults = childDefaultWater is { } height ? new WorldspaceLandDefaults { DefaultWaterHeight = height } : null,
        };
        if (cell != null) worldspace.AddCell(cell);
        mod.Mod.Worldspaces.Add(worldspace);
    }

    private Cell AddCell(FixtureMod mod, float? landOffset)
    {
        var cell = new Cell(_cell, FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        if (landOffset is { } offset)
        {
            cell.Landscape = new Landscape(mod.NextKey(), FixtureMod.Release) { VertexHeightMap = new LandscapeVertexHeightMap { Offset = offset } };
        }
        var worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        worldspace.AddCell(cell);
        mod.Mod.Worldspaces.Add(worldspace);
        return cell;
    }

    private IPluginRecords OpenPlugin()
    {
        var state = FixturePatcherState.Create([_base.Mod, _target.Mod, _noLand.Mod, _late.Mod], Path.GetTempPath(), Path.GetTempPath());
        return new PluginRecordsFactory(FixturePatcherState.CreateFinalWorld(state), Environment.ProcessorCount).Open();
    }
}
