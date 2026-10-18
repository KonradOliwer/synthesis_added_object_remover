using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A load order that holds only the given placed record facts and links; every other read throws.</summary>
internal sealed class FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records, ImmutableArray<LinkFact> links) : IPluginRecords
{
    public FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records)
        : this(records, [])
    {
    }

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) =>
        new(records, records.Length, new HashSet<RecordKey>(), links, NavmeshCount: 0, UnnamedCellsOf(records));

    /// <summary>Each cell of the records once, without grid, name or Location.</summary>
    private static Dictionary<RecordKey, CellAreaFact> UnnamedCellsOf(ImmutableArray<PlacedRecordFact> records) =>
        records
            .DistinctBy(record => record.Cell.Key)
            .ToDictionary(record => record.Cell.Key, record => new CellAreaFact(record.Space.Key, Grid: null, Name: null, Location: null));

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => [];

    public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

    public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();
    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

    public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
