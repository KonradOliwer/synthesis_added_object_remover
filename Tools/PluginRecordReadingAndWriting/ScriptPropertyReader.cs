using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads what the properties of a record's scripts name.</summary>
internal static class ScriptPropertyReader
{
    /// <summary>Resolved by these types only; <see cref="IMajorRecordGetter"/> would make the link cache enumerate every record of every mod.</summary>
    private static readonly ImmutableArray<(Type Type, ScriptNamedKind Kind)> NamedKinds =
    [
        (typeof(IQuestGetter), ScriptNamedKind.Quest),
        (typeof(ISceneGetter), ScriptNamedKind.Scene),
    ];

    /// <returns>The first quest or scene a script property names, in script and property order; null when none does.</returns>
    public static ScriptNamedRecord? FindQuestOrScene(IAVirtualMachineAdapterGetter? adapter, ILinkCache linkCache, PluginNames names)
    {
        if (adapter is null) return null;
        var links = MutagenCalls.Read(() => adapter.Scripts.SelectMany(script => script.EnumerateFormLinks()).Select(link => link.FormKey).ToArray());
        foreach (var link in links)
        {
            if (link.IsNull) continue;
            foreach (var (type, kind) in NamedKinds)
            {
                var named = MutagenCalls.Read(() => linkCache.TryResolve(link, type, out var record) ? (record.FormKey, record.EditorID) : ((FormKey, string?)?)null);
                if (named is (var key, var editorId)) return new ScriptNamedRecord(names.KeyOf(key), editorId, kind);
            }
        }
        return null;
    }
}
