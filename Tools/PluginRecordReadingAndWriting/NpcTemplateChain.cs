using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Walks NPC template chains and leveled lists in the records. Thread-safe, like the link cache.</summary>
internal sealed class NpcTemplateChain(ILinkCache linkCache, PluginNames names)
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
            var npc = ReadNpcTemplate(spawnKey, followed);
            if (!npc.IsNpc)
            {
                return IsLeveledList(spawnKey) ? new TraitSupplier(spawn, TraitSupplierKind.LeveledList) : null;
            }
            if (npc.Template is not { } template) return new TraitSupplier(spawn, TraitSupplierKind.Npc);
            spawn = names.KeyOf(template);
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
        var npc = ReadNpcTemplate(spawnKey, followed);
        if (npc.IsNpc)
        {
            if (npc.Template is { } template) CollectTraitSources(names.KeyOf(template), followed, visited, sources);
            else sources.Add(spawn);
            return;
        }
        foreach (var entry in ReadLeveledEntries(spawnKey))
        {
            CollectTraitSources(names.KeyOf(entry), followed, visited, sources);
        }
    }

    private static NpcConfiguration.TemplateFlag ToRecordFlag(NpcTemplateFlag flag) => flag switch
    {
        NpcTemplateFlag.Traits => NpcConfiguration.TemplateFlag.Traits,
        _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, null),
    };

    /// <returns>
    /// Whether the record is an NPC, and the template it takes the followed traits from; null when it supplies them itself.
    /// The template flag has no effect without a template.
    /// </returns>
    private (bool IsNpc, FormKey? Template) ReadNpcTemplate(FormKey npcKey, NpcConfiguration.TemplateFlag followed) =>
        MutagenCalls.Read(() => linkCache.TryResolve<INpcGetter>(npcKey, out var npc)
            ? (true, npc.Configuration.TemplateFlags.HasFlag(followed) && !npc.Template.IsNull ? npc.Template.FormKey : (FormKey?)null)
            : (false, null));

    private bool IsLeveledList(FormKey key) => MutagenCalls.Read(() => linkCache.TryResolve<ILeveledNpcGetter>(key, out _));

    /// <returns>The records the leveled list spawns, in the order listed; empty when the record is not a leveled list.</returns>
    private FormKey[] ReadLeveledEntries(FormKey listKey) =>
        MutagenCalls.Read(() => linkCache.TryResolve<ILeveledNpcGetter>(listKey, out var list)
            ? (list.Entries ?? []).Select(entry => entry.Data is { Reference.IsNull: false } data ? data.Reference.FormKey : (FormKey?)null).OfType<FormKey>().ToArray()
            : []);
}
