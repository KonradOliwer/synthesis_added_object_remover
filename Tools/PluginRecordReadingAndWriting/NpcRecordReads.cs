using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover;

/// <summary>The NPC reads of <see cref="IPluginRecords"/>, answered from a link cache. Thread-safe.</summary>
internal sealed class NpcRecordReads
{
    private readonly NpcTemplateChain _templateChain;
    private readonly NpcTraitsReader _npcTraits;

    public NpcRecordReads(ILinkCache linkCache, KnownPluginNames knownPlugins)
    {
        var names = new PluginNames(knownPlugins);
        _templateChain = new NpcTemplateChain(linkCache, names);
        _npcTraits = new NpcTraitsReader(linkCache, names);
    }

    public NpcTraits? NpcTraitsOf(BaseKey npc) => _npcTraits.Read(npc.Record);

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => _templateChain.FindTraitSupplier(spawn.Record, templateFlag);

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => _templateChain.CollectTraitSources(list, templateFlag);
}
