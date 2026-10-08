using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Converts between Mutagen's keys and ours where records are read from or written to the load order.</summary>
internal static class RecordKeyConversion
{
    public static PluginName ToPluginName(this ModKey key) => new(key.FileName.String);

    public static RecordKey ToRecordKey(this FormKey key) => new(key.ModKey.ToPluginName(), key.ID);

    public static FormKey ToFormKey(this RecordKey key) => new(ModKey.FromFileName(key.Plugin.FileName), key.Id);
}

/// <summary>
/// Hands out one <see cref="PluginName"/> per plugin, so converting millions of record keys does not build a
/// string each. Owned by one run's single-threaded reader.
/// </summary>
internal sealed class PluginNames
{
    private readonly Dictionary<ModKey, PluginName> _byMod = [];

    public PluginName Of(ModKey mod)
    {
        if (!_byMod.TryGetValue(mod, out var name))
        {
            name = mod.ToPluginName();
            _byMod[mod] = name;
        }
        return name;
    }

    public RecordKey KeyOf(FormKey key) => new(Of(key.ModKey), key.ID);
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
