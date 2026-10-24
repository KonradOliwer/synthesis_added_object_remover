using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Answers the NPC reads from in-memory records through the real readers; nothing else is read.</summary>
internal sealed class NpcRecordsPlugin(ILinkCache linkCache) : IPluginRecords
{
    private readonly NpcRecordReads _npcReads = new(linkCache, new PluginNames(new KnownPluginNames(linkCache.ListedOrder.Select(mod => mod.ModKey.ToPluginName()))));

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcReads.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _npcReads.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _npcReads.LeveledEntriesOf(list, templateFlag);

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => throw new NotSupportedException();

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

    public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

    public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();
    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

    public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
