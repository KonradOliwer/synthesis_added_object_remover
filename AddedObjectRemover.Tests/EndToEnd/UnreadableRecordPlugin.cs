namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The plugin records of a run, except that reading the links also meets one record the game library cannot parse.</summary>
internal sealed class UnreadableRecordPlugin(IPluginRecords inner) : IPluginRecords
{
    public static readonly UnreadableRecord Record = new(new RecordKey(new PluginName("Other.esp"), 0x10), "BrokenPerk", "Perk", "it is malformed");

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => inner.ReadPlacedRecords(scope, stopsTheWalk);

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets)
    {
        var read = inner.ReadLinks(target, targets);
        return read with { Unreadable = read.Unreadable.Add(Record) };
    }

    public IWritableRecords ReadWritableRecords() => inner.ReadWritableRecords();

    public IRecordIndex ReadRecordIndex() => inner.ReadRecordIndex();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => inner.ReadHomeCellGrid(record);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => inner.ReadTerrainVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => inner.ReadWaterCopies(cell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => inner.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => inner.FindLandWorldspace(space);

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
