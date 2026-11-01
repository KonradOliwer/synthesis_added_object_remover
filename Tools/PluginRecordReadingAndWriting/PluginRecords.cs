using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
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

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> records, IReadOnlySet<PluginName> linkedPlugins)
    {
        var linkedOrTarget = linkedPlugins.Append(target).ToHashSet();
        var referencingMods = order.PriorityOrder
            .Select(listing => listing.Mod)
            .OfType<ISkyrimModGetter>()
            .Where(mod => LinkablePlugins.MayLinkToAny(mod, linkedOrTarget, patch, names))
            .ToList();
        return LinkReader.ReadFromNonPlaced(referencingMods, records, target, names, execution);
    }

    public IWritableRecords ReadWritableRecords() => Scanned.Handles;

    public IRecordIndex ReadRecordIndex() => runCache;

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) =>
        ReadLazily(() => Scanned.Handles.LocationOf(record).WinningCell.Grid is { } grid ? new CellGridPoint(grid.Point.X, grid.Point.Y) : (CellGridPoint?)null);

    public float[]? ReadTerrain(ExteriorCell cell) => ReadLazily(() => Scanned.Terrain.ReadHeights(cell));

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => ReadLazily(() => Scanned.Terrain.ReadHeightsBelow(plugin, cell));

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => ReadLazily(() => Scanned.Terrain.ReadVersions(cell));

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => ReadLazily(() => Scanned.Water.ReadCopies(cell));

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => ReadLazily(() => _materials.ChainOf(texture));

    public RecordKey? FindLandWorldspace(RecordKey space) => ReadLazily(() => Scanned.Terrain.FindLandWorldspace(space));

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => ReadLazily(() => Scanned.Navmeshes.ReadTriangles(bucket));

    public NpcTraits? NpcTraitsOf(BaseKey npc) => ReadLazily(() => _npcReads.NpcTraitsOf(npc));

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => ReadLazily(() => _npcReads.TraitSupplierOf(spawn, templateFlag));

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => ReadLazily(() => _npcReads.LeveledEntriesOf(list, templateFlag));

    public bool TryParsePluginName(string text, out PluginName name) => PluginNameParser.TryParsePluginName(text, out name);

    /// <remarks>Mutagen reads a record's subrecords lazily, so a malformed one throws when the read reaches it; that is a failure of the user's data.</remarks>
    private static T ReadLazily<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (MalformedDataException ex)
        {
            throw new MalformedRecordException(ex);
        }
    }

    private ScanLookups Scanned => _scanned ?? throw new InvalidOperationException(NotReadMessage);
}
