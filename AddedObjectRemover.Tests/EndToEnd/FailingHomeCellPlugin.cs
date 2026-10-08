using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugin records of a run, except that finding a record's home cell throws: only the marker moves ask for it,
/// so it stands for an unexpected error inside that one part.
/// </summary>
internal sealed class FailingHomeCellPlugin(IPluginRecords inner) : IPluginRecords
{
    public const string FailureMessage = "home cells broke";

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => inner.ReadPlacedRecords(scope);

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => inner.ReadLinks(target, targets);

    public bool HasEnableParent(RecordKey record) => inner.HasEnableParent(record);

    public void Write(IReadOnlyList<WriteOrder> orders) => inner.Write(orders);

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new InvalidOperationException(FailureMessage);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public RecordKey? FindLandWorldspace(RecordKey space) => inner.FindLandWorldspace(space);

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
