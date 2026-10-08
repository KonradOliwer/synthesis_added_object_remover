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
