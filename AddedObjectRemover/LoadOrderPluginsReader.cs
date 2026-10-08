using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Reads the load order's plugin names, masters and whether each could be read; once per run, before the settings are checked.</summary>
internal static class LoadOrderPluginsReader
{
    public static LoadOrderPlugins Read(ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder, ModKey patch)
    {
        var listings = loadOrder.ListedOrder
            .Select(listing => (Plugin: listing.ModKey.ToPluginName(), Masters: ReadMasters(listing.Mod)))
            .ToList();
        return Create(listings, patch.ToPluginName());
    }

    /// <param name="listings">In load order; the masters are null when the plugin could not be read.</param>
    /// <remarks>Masters are printed as the load order spells them, even if the plugin names them in another letter case.</remarks>
    internal static LoadOrderPlugins Create(IReadOnlyList<(PluginName Plugin, List<PluginName>? Masters)> listings, PluginName patch)
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
                [.. (listing.Masters ?? []).Select(table.WithKnownSpelling)]))]);
    }

    /// <returns>Null when the plugin could not be read.</returns>
    private static List<PluginName>? ReadMasters(ISkyrimModGetter? mod) =>
        mod?.MasterReferences.Select(master => master.Master.ToPluginName()).ToList();
}
