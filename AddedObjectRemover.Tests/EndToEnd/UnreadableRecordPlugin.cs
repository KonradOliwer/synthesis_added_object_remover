namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugin records of a run, except that reading the links also meets one record the game library cannot parse, and the plugins
/// the links were read for are kept.
/// </summary>
internal sealed class UnreadableRecordPlugin(IPluginRecords inner) : IPluginRecords
{
    public static readonly UnreadableRecord Record = new(new RecordKey(new PluginName("Other.esp"), 0x10), "BrokenPerk", "Perk", "it is malformed");

    /// <summary>The plugins whose dependents the last link read covered; none before a link read.</summary>
    public IReadOnlySet<PluginName> LinkedPlugins { get; private set; } = new HashSet<PluginName>();

    /// <summary>The records the last link read was asked for; none before a link read.</summary>
    public IReadOnlySet<RecordKey> LinkedRecords { get; private set; } = new HashSet<RecordKey>();

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => inner.ReadPlacedRecords(scope, stopsTheWalk);

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins)
    {
        LinkedRecords = targets;
        LinkedPlugins = linkedPlugins;
        var read = inner.ReadLinks(target, targets, linkedPlugins);
        return read with { Unreadable = read.Unreadable.Add(Record) };
    }

    public IWritableRecords ReadWritableRecords() => inner.ReadWritableRecords();

    public IRecordIndex ReadRecordIndex() => inner.ReadRecordIndex();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => inner.ReadHomeCellGrid(record);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => inner.ReadTerrainBelow(plugin, cell);

    public PluginName? ReadTerrainWinner(ExteriorCell cell) => inner.ReadTerrainWinner(cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => inner.ReadTerrainVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => inner.ReadWaterCopies(cell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => inner.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => inner.FindLandWorldspace(space);

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
