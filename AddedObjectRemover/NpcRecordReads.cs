using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover;

/// <summary>The NPC reads of <see cref="IPluginRecords"/>, answered from a link cache. Thread-safe.</summary>
internal sealed class NpcRecordReads(ILinkCache linkCache)
{
    private readonly NpcTemplateChain _templateChain = new(linkCache);
    private readonly NpcTraitsReader _npcTraits = new(linkCache);

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcTraits.Read(npc.Record);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _templateChain.FindTraitSupplier(spawn.Record, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _templateChain.CollectTraitSources(list, templateFlag);
}
