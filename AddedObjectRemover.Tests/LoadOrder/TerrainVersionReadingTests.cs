using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>The reader keeps each plugin's own land record of a cell, in load order, only when the scope asks for the versions.</summary>
public class TerrainVersionReadingTests
{
    private const float BaseOffset = 10f;
    private const float TargetOffset = 12f;
    private const float LateOffset = 20f;
    private const float UnitsPerHeightStep = 8f;

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
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, TestRecordKeys.HoldKeyword));

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
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: true, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, TestRecordKeys.HoldKeyword));

        Assert.Equal(LateOffset * UnitsPerHeightStep, plugin.ReadTerrain(ExteriorCell)![0]);
    }

    [Fact]
    public void VersionsAreNotKeptUnlessAskedFor()
    {
        var plugin = OpenPlugin();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: true, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, TestRecordKeys.HoldKeyword));

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
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: false, TestRecordKeys.HoldKeyword));

        Assert.Equal(_worldspace.ToRecordKey(), plugin.FindLandWorldspace(child.ToRecordKey()));
        Assert.Equal(
            [_base.Key.ToPluginName(), _target.Key.ToPluginName(), _late.Key.ToPluginName()],
            plugin.ReadTerrainVersions(ExteriorCell).Select(version => version.Plugin));
    }

    private void AddChildWorldspace(FixtureMod mod, FormKey child, Cell? cell)
    {
        var worldspace = new Worldspace(child, FixtureMod.Release)
        {
            Parent = new WorldspaceParent { Worldspace = new FormLink<IWorldspaceGetter>(_worldspace), Flags = WorldspaceParent.Flag.UseLandData },
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
        var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
        return new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod, plugins.Mods, Environment.ProcessorCount).Open();
    }
}
