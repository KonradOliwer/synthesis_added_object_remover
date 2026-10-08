using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Walks NPC template chains and leveled lists in the records. Thread-safe, like the link cache.</summary>
internal sealed class NpcTemplateChain(ILinkCache linkCache)
{
    /// <summary>
    /// The record that supplies a placed base's traits, found along the template chain: an NPC
    /// supplying its own traits, a leveled list, or null when the chain breaks or loops.
    /// </summary>
    public TraitSupplier? FindTraitSupplier(RecordKey spawn, NpcTemplateFlag flag)
    {
        var followed = ToRecordFlag(flag);
        var visited = new HashSet<RecordKey>();
        while (visited.Add(spawn))
        {
            var spawnKey = spawn.ToFormKey();
            if (!linkCache.TryResolve<INpcGetter>(spawnKey, out var npc))
            {
                return linkCache.TryResolve<ILeveledNpcGetter>(spawnKey, out _) ? new TraitSupplier(spawn, TraitSupplierKind.LeveledList) : null;
            }
            if (!InheritsTraits(npc, followed)) return new TraitSupplier(spawn, TraitSupplierKind.Npc);
            spawn = npc.Template.FormKey.ToRecordKey();
        }
        return null;
    }

    /// <summary>
    /// The NPCs supplying their own traits that a leveled list can spawn, through nested lists and
    /// templates. Each record is visited once, so loops end.
    /// </summary>
    public List<RecordKey> CollectTraitSources(RecordKey list, NpcTemplateFlag flag)
    {
        var sources = new List<RecordKey>();
        CollectTraitSources(list, ToRecordFlag(flag), [], sources);
        return sources;
    }

    private void CollectTraitSources(RecordKey spawn, NpcConfiguration.TemplateFlag followed, HashSet<RecordKey> visited, List<RecordKey> sources)
    {
        if (!visited.Add(spawn)) return;
        var spawnKey = spawn.ToFormKey();
        if (linkCache.TryResolve<INpcGetter>(spawnKey, out var npc))
        {
            if (InheritsTraits(npc, followed)) CollectTraitSources(npc.Template.FormKey.ToRecordKey(), followed, visited, sources);
            else sources.Add(spawn);
            return;
        }
        if (!linkCache.TryResolve<ILeveledNpcGetter>(spawnKey, out var list)) return;
        foreach (var entry in list.Entries ?? [])
        {
            if (entry.Data is { Reference.IsNull: false } data) CollectTraitSources(data.Reference.FormKey.ToRecordKey(), followed, visited, sources);
        }
    }

    private static NpcConfiguration.TemplateFlag ToRecordFlag(NpcTemplateFlag flag) => flag switch
    {
        NpcTemplateFlag.Traits => NpcConfiguration.TemplateFlag.Traits,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
    };

    /// <summary>The template flag has no effect without a template.</summary>
    private static bool InheritsTraits(INpcGetter npc, NpcConfiguration.TemplateFlag followed) =>
        npc.Configuration.TemplateFlags.HasFlag(followed) && !npc.Template.IsNull;
}
