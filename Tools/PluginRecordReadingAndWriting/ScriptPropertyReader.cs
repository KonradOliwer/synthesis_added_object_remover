using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads what the properties of a record's scripts name.</summary>
internal static class ScriptPropertyReader
{
    /// <summary>Resolved by these types only; <see cref="IMajorRecordGetter"/> would make the link cache enumerate every record of every mod.</summary>
    private static readonly (Type Type, ScriptNamedKind Kind)[] NamedKinds =
    [
        (typeof(IQuestGetter), ScriptNamedKind.Quest),
        (typeof(ISceneGetter), ScriptNamedKind.Scene),
    ];

    /// <returns>The first quest or scene a script property names, in script and property order; null when none does.</returns>
    public static ScriptNamedRecord? FindQuestOrScene(IAVirtualMachineAdapterGetter? adapter, ILinkCache linkCache, PluginNames names)
    {
        if (adapter is null) return null;
        foreach (var link in adapter.Scripts.SelectMany(script => script.EnumerateFormLinks()))
        {
            if (link.IsNull) continue;
            foreach (var (type, kind) in NamedKinds)
            {
                if (linkCache.TryResolve(link.FormKey, type, out var record)) return new ScriptNamedRecord(names.KeyOf(record.FormKey), record.EditorID, kind);
            }
        }
        return null;
    }
}
