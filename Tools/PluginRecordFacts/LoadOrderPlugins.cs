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
public sealed record PluginListing(PluginName Mod, bool Loaded, ImmutableArray<PluginName> Masters)
{
    /// <summary>Whether the plugin's header description holds the patch marker text.</summary>
    public bool CarriesPatchMarker { get; init; }
}

/// <summary>An entry of the game's plugin list, enabled or not.</summary>
public sealed record PluginListEntry(PluginName Plugin, bool Enabled);

/// <summary>The load order as read once, before the settings are checked.</summary>
/// <param name="Listed">The listed plugins, lowest priority first, the patch plugin included.</param>
/// <param name="PluginsFilePath">The file the game's plugin list was read from.</param>
/// <param name="Positions">
/// The position of every entry of the game's plugin list, enabled or not. These are plain list positions: the game's
/// master-first reordering is not modelled.
/// </param>
/// <param name="DisabledPlugins">The entries of the game's plugin list that are not enabled.</param>
/// <param name="MissingPlugins">The enabled entries below the patch whose file was not found; they are not in <paramref name="Listed"/>.</param>
public sealed record LoadOrderPlugins(
    KnownPluginNames Mods,
    PluginName PatchMod,
    ImmutableArray<PluginListing> Listed,
    string PluginsFilePath,
    LoadOrderPositions Positions,
    ImmutableHashSet<PluginName> DisabledPlugins,
    ImmutableArray<PluginName> MissingPlugins)
{
    /// <summary>The position of the patch's entry in the game's plugin list; the end of the list when the patch has no entry.</summary>
    public int PatchPosition => Positions.Contains(PatchMod) ? Positions.Of(PatchMod) : Positions.Count;

    public PluginListing? Find(PluginName mod) => Listed.FirstOrDefault(listing => listing.Mod == mod);

    /// <summary>Whether the plugin is listed after the patch in the game's plugin list, so its records beat what the patch writes.</summary>
    public bool LoadsAfterPatch(PluginName plugin) => plugin != PatchMod && Positions.Contains(plugin) && Positions.Of(plugin) > PatchPosition;

    /// <summary>The listed plugins that have the mod as a master, in the order of <see cref="Listed"/>; the patch is left out.</summary>
    public ImmutableArray<PluginListing> ListingsRequiring(PluginName mod) =>
        [.. Listed.Where(listing => listing.Mod != PatchMod && listing.Masters.Contains(mod))];
}

/// <summary>The position of each plugin in a list, found without walking the load order.</summary>
public sealed class LoadOrderPositions
{
    private readonly Dictionary<PluginName, int> _positions = [];

    /// <param name="listed">The listed plugins, lowest priority first.</param>
    public LoadOrderPositions(ImmutableArray<PluginListing> listed) : this(listed.Select(listing => listing.Mod))
    {
    }

    /// <param name="inOrder">The plugins, lowest priority first.</param>
    public LoadOrderPositions(IEnumerable<PluginName> inOrder)
    {
        foreach (var plugin in inOrder) _positions[plugin] = Count++;
    }

    /// <summary>The number of entries, which is the position after the last one.</summary>
    public int Count { get; }

    public bool Contains(PluginName plugin) => _positions.ContainsKey(plugin);

    /// <returns>A higher position for a plugin loading later; the last position of a plugin listed twice.</returns>
    public int Of(PluginName plugin) => _positions[plugin];
}
