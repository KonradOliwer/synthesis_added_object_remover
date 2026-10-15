using System.Collections.Concurrent;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Converts between Mutagen's keys and ours where records are read from or written to the load order.</summary>
internal static class RecordKeyConversion
{
    public static PluginName ToPluginName(this ModKey key) => new(key.FileName.String);

    public static FormKey ToFormKey(this RecordKey key) => new(ModKey.FromFileName(key.Plugin.FileName), key.Id);
}

/// <summary>
/// Hands out one <see cref="PluginName"/> per plugin, spelled as the run's known plugin names spell it, so every
/// record key prints alike and converting millions of record keys does not build a string each. Thread-safe.
/// </summary>
internal sealed class PluginNames(KnownPluginNames known)
{
    private readonly ConcurrentDictionary<ModKey, PluginName> _byMod = new();

    public PluginName Of(ModKey mod) => _byMod.GetOrAdd(mod, static (key, names) => SpellingOf(key, names), known);

    public RecordKey KeyOf(FormKey key) => key == FormKey.None ? RecordKey.None : key.IsNull ? RecordKey.Null : new(Of(key.ModKey), key.ID);

    private static PluginName SpellingOf(ModKey mod, KnownPluginNames known)
    {
        var plugin = mod.ToPluginName();
        return known.Knows(plugin) ? known.WithKnownSpelling(plugin) : throw new InvalidOperationException($"Plugin {plugin} is not in the load order's known plugin names.");
    }
}

/// <summary>Reads plugin file names by Mutagen's rules: its accepted extensions and characters, with the extension in lower case.</summary>
internal static class PluginNameParser
{
    public static bool TryParsePluginName(string text, out PluginName name)
    {
        var parsed = ModKey.TryFromNameAndExtension(text, out var key);
        name = parsed ? key.ToPluginName() : default;
        return parsed;
    }
}
