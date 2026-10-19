using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// Every plugin a run knows of: the listed load order, the patch plugin, then masters that are not listed.
/// Names that differ only in letter case are one plugin, printed with the spelling seen first.
/// </summary>
public sealed class KnownPluginNames
{
    private readonly Dictionary<PluginName, PluginName> _firstSpelling = [];

    public KnownPluginNames(IEnumerable<PluginName> plugins)
    {
        foreach (var plugin in plugins) _firstSpelling.TryAdd(plugin, plugin);
    }

    public bool Knows(PluginName plugin) => _firstSpelling.ContainsKey(plugin);

    public PluginName WithKnownSpelling(PluginName plugin) => _firstSpelling[plugin];
}

/// <param name="Loaded">False when the plugin is listed but could not be read.</param>
/// <param name="Masters">Empty when the plugin could not be read.</param>
public sealed record PluginListing(PluginName Mod, bool Loaded, ImmutableArray<PluginName> Masters);

/// <summary>The load order as read once, before the settings are checked.</summary>
/// <param name="Listed">The listed plugins, lowest priority first, the patch plugin included.</param>
public sealed record LoadOrderPlugins(KnownPluginNames Mods, PluginName PatchMod, ImmutableArray<PluginListing> Listed)
{
    public PluginListing? Find(PluginName mod) => Listed.FirstOrDefault(listing => listing.Mod == mod);
}

/// <summary>The load-order position of each listed plugin, found without walking the load order.</summary>
public sealed class LoadOrderPositions
{
    private readonly Dictionary<PluginName, int> _positions = [];

    /// <param name="listed">The listed plugins, lowest priority first.</param>
    public LoadOrderPositions(ImmutableArray<PluginListing> listed)
    {
        for (var position = 0; position < listed.Length; position++) _positions[listed[position].Mod] = position;
    }

    /// <returns>A higher position for a plugin loading later; the last position of a plugin listed twice.</returns>
    public int Of(PluginName plugin) => _positions[plugin];
}
