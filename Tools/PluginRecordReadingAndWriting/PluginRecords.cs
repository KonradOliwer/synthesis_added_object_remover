using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the final world's plugin records through Mutagen, resolving through the run's own link cache.</summary>
internal sealed class PluginRecords(
    ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> order,
    RunLinkCache runCache,
    ModKey patch,
    PluginNames names,
    Execution execution) : IPluginRecords
{
    private const string NotReadMessage = "The placed records must be read before they are used.";

    private readonly NpcRecordReads _npcReads = new(runCache.Cache, names);
    private readonly LandscapeMaterials _materials = new(runCache.Cache, names);
    private ScanLookups? _scanned;

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk)
    {
        var scanned = PlacedRecordReader.Read(order, runCache.Cache, patch, names, scope, stopsTheWalk, execution);
        _scanned = scanned.Lookups;
        return scanned.Facts;
    }

    public NonPlacedLinks ReadLinks(PluginName plugin, IReadOnlySet<RecordKey> records, IReadOnlySet<PluginName> linkedPlugins) =>
        ReadLinksFromLinkablePlugins(plugin, records, linkedPlugins);

    public IWritableRecords ReadWritableRecords() => Scanned.Handles;

    public IRecordIndex ReadRecordIndex() => runCache;

    public CellGridPoint? ReadHomeCellGrid(RecordKey record)
    {
        var cell = Scanned.Handles.LocationOf(record).WinningCell;
        return MutagenCalls.Read(() => cell.Grid?.Point) is { } point ? new CellGridPoint(point.X, point.Y) : null;
    }

    public float[]? ReadTerrain(ExteriorCell cell) => Scanned.Terrain.ReadHeights(cell);

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => Scanned.Terrain.ReadHeightsBelow(plugin, cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => Scanned.Terrain.ReadVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => Scanned.Water.ReadCopies(cell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => _materials.ChainOf(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => Scanned.Terrain.FindLandWorldspace(space);

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => Scanned.Navmeshes.ReadTriangles(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcReads.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _npcReads.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _npcReads.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => PluginNameParser.TryParsePluginName(text, out name);

    private NonPlacedLinks ReadLinksFromLinkablePlugins(PluginName plugin, IReadOnlySet<RecordKey> records, IReadOnlySet<PluginName> linkedPlugins)
    {
        var linkedOrPlugin = linkedPlugins.Append(plugin).ToHashSet();
        var referencingMods = order.PriorityOrder
            .Select(listing => listing.Mod)
            .OfType<ISkyrimModGetter>()
            .Where(mod => LinkablePlugins.MayLinkToAny(mod, linkedOrPlugin, patch, names))
            .ToList();
        return LinkReader.ReadFromNonPlaced(referencingMods, records, plugin, names, execution);
    }

    private ScanLookups Scanned => _scanned ?? throw new InvalidOperationException(NotReadMessage);
}
