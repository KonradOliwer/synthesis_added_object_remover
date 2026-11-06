using Mutagen.Bethesda.Plugins.Exceptions;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugin records of a run, except that finding the worldspace whose land a space uses meets a record the game library cannot
/// parse: in the village world with the changed-terrain step off, only the restore step reaches it, so it stands for a malformed
/// record inside that one step.
/// </summary>
internal sealed class MalformedLandWorldspacePlugin(IPluginRecords inner) : IPluginRecords
{
    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => inner.ReadPlacedRecords(scope, stopsTheWalk);

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) =>
        inner.ReadLinks(target, targets, linkedPlugins);

    public IWritableRecords ReadWritableRecords() => inner.ReadWritableRecords();

    public IRecordIndex ReadRecordIndex() => inner.ReadRecordIndex();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => inner.ReadHomeCellGrid(record);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => inner.ReadTerrainBelow(plugin, cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => inner.ReadTerrainVersions(cell);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => inner.ReadWaterCopies(cell);

    public ExteriorCell? FindExteriorCell(RecordKey landOrCell) => inner.FindExteriorCell(landOrCell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => inner.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) =>
        throw new MalformedRecordException(new MalformedDataException("The bytes cannot be parsed."));

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
