using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Reads plugin names like the real plugin records; every record read or write is refused.</summary>
internal sealed class PluginNameParsingPlugin : IPluginRecords
{
    public bool TryParsePluginName(string text, out PluginName name) => PluginNameParser.TryParsePluginName(text, out name);

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

    public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

    public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

    public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();
}
