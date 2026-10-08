using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the load order's plugin records through Mutagen and writes the patch.</summary>
internal sealed class PluginRecords(
    ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    ISkyrimMod patchMod) : IPluginRecords
{
    private const string NotReadMessage = "The placed records must be read before they are used.";

    private readonly PluginNames _names = new();
    private readonly NpcRecordReads _npcReads = new(linkCache);
    private PlacedRecordWriter? _writer;
    private ScannedRecords? _scanned;

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope)
    {
        var scanned = PlacedRecordReader.Read(loadOrder, linkCache, patchMod.ModKey, _names, scope);
        _scanned = scanned;
        _writer = new PlacedRecordWriter(new PlacedOverrideWriter(patchMod), scanned.Handles);
        return scanned.Facts;
    }

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets)
    {
        var referencingMods = loadOrder.PriorityOrder
            .Select(listing => listing.Mod)
            .OfType<ISkyrimModGetter>()
            .Where(mod => PlacedRecordReader.MayReferenceTarget(mod, target, patchMod.ModKey, _names));
        var facts = new List<LinkFact>();
        LinkReader.ReadFromNonPlaced(referencingMods, targets, target, _names, facts);
        return [.. facts];
    }

    public bool HasEnableParent(RecordKey record) => Writer.HasEnableParent(record);

    public void Write(IReadOnlyList<WriteOrder> orders) => Writer.Write(orders);

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) =>
        Scanned.Handles.LocationOf(record).WinningCell.Record.Grid is { } grid ? new CellGridPoint(grid.Point.X, grid.Point.Y) : null;

    public float[]? ReadTerrain(ExteriorCell cell) => Scanned.Terrain.ReadHeights(cell);

    public RecordKey? FindLandWorldspace(RecordKey space) => Scanned.Terrain.FindLandWorldspace(space);

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => Scanned.Navmeshes.ReadTriangles(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcReads.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _npcReads.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _npcReads.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => PluginNameParser.TryParsePluginName(text, out name);

    private PlacedRecordWriter Writer => _writer ?? throw new InvalidOperationException(NotReadMessage);

    private ScannedRecords Scanned => _scanned ?? throw new InvalidOperationException(NotReadMessage);
}
