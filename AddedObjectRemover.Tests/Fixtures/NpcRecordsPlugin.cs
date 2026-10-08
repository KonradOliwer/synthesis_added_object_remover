using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Answers the NPC reads from in-memory records through the real readers; nothing else is read.</summary>
internal sealed class NpcRecordsPlugin(ILinkCache linkCache) : IPluginRecords
{
    private readonly NpcRecordReads _npcReads = new(linkCache);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcReads.NpcTraitsOf(npc);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _npcReads.TraitSupplierOf(spawn, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _npcReads.LeveledEntriesOf(list, templateFlag);

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

    public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

    public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

    public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

    public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

    public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
