using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>The reader decodes each land version's texture layers only when the scope asks, and walks a texture's material chain.</summary>
public class TerrainLayerReadingTests
{
    private const float HalfOpacity = 0.5f;
    private const int AlphaPosition = 3;
    private const int AlphaLayerNumber = 1;

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _target;
    private readonly FormKey _worldspace;
    private readonly FormKey _cell;
    private readonly FormKey _grass;
    private readonly FormKey _gravel;
    private readonly List<Landscape> _landscapes = [];

    public TerrainLayerReadingTests()
    {
        _target = new FixtureMod("Target.esp", _base);
        _worldspace = _base.NextKey();
        _cell = _base.NextKey();
        var root = AddMaterial(parent: null);
        var child = AddMaterial(parent: root);
        _grass = AddTexture(child);
        _gravel = AddTexture(root);
        AddCellWithLayers(_base);
        var targetCell = AddCellWithLayers(_target);
        var placed = new PlacedObject(_target.NextKey(), FixtureMod.Release);
        placed.Base.SetTo(_base.AddMarkerStatic("BaseStatic"));
        targetCell.Temporary.Add(placed);
    }

    private ExteriorCell ExteriorCell => new(_worldspace.ToRecordKey(), 0, 0);

    [Fact]
    public void VersionsHoldTheDecodedLayersOfEveryQuadrant()
    {
        var plugin = OpenPlugin(layers: true);

        var layers = plugin.ReadTerrainVersions(ExteriorCell)[0].Layers!;

        Assert.Equal(_grass.ToRecordKey(), layers.Quadrants[TerrainQuadrants.IndexOf(0, 0)].Base);
        Assert.Null(layers.Quadrants[TerrainQuadrants.IndexOf(1, 0)].Base);
        var alpha = Assert.Single(layers.Quadrants[TerrainQuadrants.IndexOf(1, 1)].Alphas);
        Assert.Equal(_gravel.ToRecordKey(), alpha.Texture);
        Assert.Equal(AlphaLayerNumber, alpha.LayerNumber);
        Assert.Equal(HalfOpacity, alpha.Opacities[AlphaPosition]);
        Assert.Equal(0f, alpha.Opacities[AlphaPosition + 1]);
        Assert.Equal(TerrainQuadrants.VertexCount, alpha.Opacities.Length);
    }

    [Fact]
    public void LayersAreNotDecodedUnlessAskedFor()
    {
        var plugin = OpenPlugin(layers: false);

        Assert.All(plugin.ReadTerrainVersions(ExteriorCell), version => Assert.Null(version.Layers));
    }

    [Fact]
    public void ALandRecordWithoutLayersHasQuadrantsWithoutLayers()
    {
        _landscapes.ForEach(landscape => landscape.Layers.Clear());
        var plugin = OpenPlugin(layers: true);

        var layers = plugin.ReadTerrainVersions(ExteriorCell)[0].Layers!;

        Assert.All(layers.Quadrants, quadrant => Assert.True(quadrant.Base is null && quadrant.Alphas.Length == 0));
    }

    [Fact]
    public void TheMaterialChainRunsFromTheTexturesMaterialUpToTheRoot()
    {
        var plugin = OpenPlugin(layers: false);

        Assert.Equal(2, plugin.ReadTextureMaterialChain(_grass.ToRecordKey()).Length);
        var rootOnly = plugin.ReadTextureMaterialChain(_gravel.ToRecordKey());
        Assert.Single(rootOnly);
        Assert.Equal(rootOnly[0], plugin.ReadTextureMaterialChain(_grass.ToRecordKey())[1]);
    }

    [Fact]
    public void ATextureThatDoesNotResolveHasNoChain()
    {
        var plugin = OpenPlugin(layers: false);

        Assert.Empty(plugin.ReadTextureMaterialChain(_base.NextKey().ToRecordKey()));
    }

    [Fact]
    public void ALayerWithAnEmptyTextureLinkReadsAsNullAndHasNoChain()
    {
        _landscapes.ForEach(landscape => landscape.Layers.Add(new BaseLayer { Header = new LayerHeader { Quadrant = Quadrant.BottomRight, LayerNumber = 0 } }));
        var plugin = OpenPlugin(layers: true);

        var layers = plugin.ReadTerrainVersions(ExteriorCell)[0].Layers!;

        var emptyTexture = layers.Quadrants[TerrainQuadrants.IndexOf(1, 0)].Base!.Value;
        Assert.Equal(RecordKey.Null, emptyTexture);
        Assert.Empty(plugin.ReadTextureMaterialChain(emptyTexture));
        Assert.Empty(plugin.ReadTextureMaterialChain(RecordKey.None));
    }

    [Fact]
    public void ATextureWithoutAMaterialHasNoChain()
    {
        var withoutMaterial = new LandscapeTexture(_base.NextKey(), FixtureMod.Release);
        _base.Mod.LandscapeTextures.Add(withoutMaterial);
        var plugin = OpenPlugin(layers: false);

        Assert.Empty(plugin.ReadTextureMaterialChain(withoutMaterial.FormKey.ToRecordKey()));
    }

    [Fact]
    public void AParentLoopEndsTheChainAtTheFirstRepeat()
    {
        var first = new MaterialType(_base.NextKey(), FixtureMod.Release);
        var second = new MaterialType(_base.NextKey(), FixtureMod.Release);
        first.Parent.SetTo(second.FormKey);
        second.Parent.SetTo(first.FormKey);
        _base.Mod.MaterialTypes.Add(first);
        _base.Mod.MaterialTypes.Add(second);
        var looping = AddTexture(first.FormKey);
        var plugin = OpenPlugin(layers: false);

        Assert.Equal([first.FormKey.ToRecordKey(), second.FormKey.ToRecordKey()], plugin.ReadTextureMaterialChain(looping.ToRecordKey()));
    }

    private FormKey AddMaterial(FormKey? parent)
    {
        var material = new MaterialType(_base.NextKey(), FixtureMod.Release);
        if (parent is { } parentKey) material.Parent.SetTo(parentKey);
        _base.Mod.MaterialTypes.Add(material);
        return material.FormKey;
    }

    private FormKey AddTexture(FormKey material)
    {
        var texture = new LandscapeTexture(_base.NextKey(), FixtureMod.Release);
        texture.MaterialType.SetTo(material);
        _base.Mod.LandscapeTextures.Add(texture);
        return texture.FormKey;
    }

    private Cell AddCellWithLayers(FixtureMod mod)
    {
        var cell = new Cell(_cell, FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var landscape = new Landscape(mod.NextKey(), FixtureMod.Release) { VertexHeightMap = new LandscapeVertexHeightMap() };
        FillLayers(landscape);
        _landscapes.Add(landscape);
        cell.Landscape = landscape;
        var worldspace = new Worldspace(_worldspace, FixtureMod.Release);
        worldspace.AddCell(cell);
        mod.Mod.Worldspaces.Add(worldspace);
        return cell;
    }

    private void FillLayers(Landscape landscape)
    {
        var baseLayer = new BaseLayer { Header = new LayerHeader { Quadrant = Quadrant.BottomLeft, LayerNumber = 0 } };
        baseLayer.Header.Texture.SetTo(_grass);
        var alphaLayer = new AlphaLayer
        {
            Header = new LayerHeader { Quadrant = Quadrant.TopRight, LayerNumber = AlphaLayerNumber },
            AlphaLayerData = [new AlphaLayerData { Position = AlphaPosition, Opacity = HalfOpacity }],
        };
        alphaLayer.Header.Texture.SetTo(_gravel);
        landscape.Layers.Add(baseLayer);
        landscape.Layers.Add(alphaLayer);
    }

    private IPluginRecords OpenPlugin(bool layers)
    {
        var state = FixturePatcherState.Create([_base.Mod, _target.Mod], Path.GetTempPath(), Path.GetTempPath());
        var plugin = new PluginRecordsFactory(FixturePatcherState.CreateFinalWorld(state), Environment.ProcessorCount).Open();
        plugin.ReadPlacedRecords(new PlacedReadScope(_target.Key.ToPluginName(), Terrain: false, Navmeshes: false, TerrainVersions: true, TerrainLayers: layers, Water: false, TestRecordKeys.HoldKeyword), TestVersionWalks.NoStop);
        return plugin;
    }
}
