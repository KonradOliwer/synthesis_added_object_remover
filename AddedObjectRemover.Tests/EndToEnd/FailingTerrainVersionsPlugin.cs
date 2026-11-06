using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The plugin records of a run, except that reading a cell's terrain versions throws: only the changed-terrain step asks for
/// them, so it stands for an unexpected error inside the check of each object.
/// </summary>
internal sealed class FailingTerrainVersionsPlugin(IPluginRecords inner) : IPluginRecords
{
    public const string FailureMessage = "terrain versions broke";

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, VersionWalkStop stopsTheWalk) => inner.ReadPlacedRecords(scope, stopsTheWalk);

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) =>
        inner.ReadLinks(target, targets, linkedPlugins);

    public IWritableRecords ReadWritableRecords() => inner.ReadWritableRecords();

    public IRecordIndex ReadRecordIndex() => inner.ReadRecordIndex();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => inner.ReadHomeCellGrid(record);

    public float[]? ReadTerrain(ExteriorCell cell) => inner.ReadTerrain(cell);

    public float[]? ReadTerrainBelow(PluginName plugin, ExteriorCell cell) => inner.ReadTerrainBelow(plugin, cell);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new PlantedUserDataException(FailureMessage);

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => inner.ReadWaterCopies(cell);

    public ExteriorCell? FindExteriorCell(RecordKey landOrCell) => inner.FindExteriorCell(landOrCell);

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => inner.ReadTextureMaterialChain(texture);

    public RecordKey? FindLandWorldspace(RecordKey space) => inner.FindLandWorldspace(space);

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => inner.ReadNavmeshes(bucket);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => inner.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => inner.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => inner.LeveledEntriesOf(list, templateFlag);

    public bool TryParsePluginName(string text, out PluginName name) => inner.TryParsePluginName(text, out name);
}
