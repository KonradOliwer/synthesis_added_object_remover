using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugin records of a run, except that reading a cell's terrain versions throws: only the changed-terrain step asks for
/// them, so it stands for an unexpected error inside the check of each object.
/// </summary>
internal sealed class FailingTerrainVersionsPlugin(IPluginRecords inner) : IPluginRecords
{
    public const string FailureMessage = "terrain versions broke";

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => inner.ReadPlacedRecords(scope);

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => inner.ReadLinks(target, targets);

    public bool HasEnableParent(RecordKey record) => inner.HasEnableParent(record);

    public void Write(IReadOnlyList<WriteOrder> orders) => inner.Write(orders);

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => inner.ReadHomeCellGrid(record);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new InvalidOperationException(FailureMessage);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => inner.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => inner.FindLandWorldspace(space);

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
