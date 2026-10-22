using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the load order's plugin names, masters and whether each could be read; once per run, before the settings are checked.</summary>
public static class LoadOrderPluginsReader
{
    public static LoadOrderPlugins Read(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder, ModKey patch, PluginListRead.Found pluginList)
    {
        var listings = loadOrder.ListedOrder
            .Select(listing => (Plugin: listing.ModKey.ToPluginName(), Masters: ReadMasters(listing.Mod)))
            .ToList();
        var entries = pluginList.Listings.Select(listing => new PluginListEntry(listing.ModKey.ToPluginName(), listing.Enabled)).ToList();
        return Create(listings, patch.ToPluginName(), pluginList.FilePath, entries);
    }

    /// <param name="listings">In load order; the masters are null when the plugin could not be read.</param>
    /// <param name="pluginList">The entries of the game's plugin list in list order.</param>
    /// <remarks>Masters are printed as the load order spells them, even if the plugin names them in another letter case.</remarks>
    internal static LoadOrderPlugins Create(
        IReadOnlyList<(PluginName Plugin, List<PluginName>? Masters)> listings,
        PluginName patch,
        string pluginsFilePath,
        IReadOnlyList<PluginListEntry> pluginList)
    {
        var table = new KnownPluginNames(listings
            .Select(listing => listing.Plugin)
            .Append(patch)
            .Concat(listings.SelectMany(listing => listing.Masters ?? [])));
        return new LoadOrderPlugins(
            table,
            patch,
            [.. listings.Select(listing => new PluginListing(
                listing.Plugin,
                listing.Masters != null,
                [.. (listing.Masters ?? []).Select(table.WithKnownSpelling)]))],
            pluginsFilePath,
            new LoadOrderPositions(pluginList.Select(entry => entry.Plugin)),
            [.. pluginList.Where(entry => !entry.Enabled).Select(entry => entry.Plugin)]);
    }

    /// <returns>Null when the plugin could not be read.</returns>
    private static List<PluginName>? ReadMasters(ISkyrimModGetter? mod) =>
        mod?.MasterReferences.Select(master => master.Master.ToPluginName()).ToList();
}
