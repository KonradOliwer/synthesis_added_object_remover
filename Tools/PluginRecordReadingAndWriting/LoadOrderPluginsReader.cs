using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Reads the final world's plugin names, masters, whether each could be read and whether its header description holds the patch
/// marker, with the game's plugin list; once, before the settings are checked.
/// </summary>
public static class LoadOrderPluginsReader
{
    /// <param name="missingPlugins">The enabled entries of the plugin list whose file was not found.</param>
    public static LoadOrderPlugins Read(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> loadOrder, ModKey patch, PluginListRead.Found pluginList, IEnumerable<PluginName> missingPlugins)
    {
        var listings = loadOrder.ListedOrder
            .Select(listing => (Plugin: listing.ModKey.ToPluginName(), Masters: ReadMasters(listing.Mod), CarriesPatchMarker: ReadPatchMarker(listing.Mod)))
            .ToList();
        var entries = pluginList.Listings.Select(listing => new PluginListEntry(listing.ModKey.ToPluginName(), listing.Enabled)).ToList();
        return Create(listings, patch.ToPluginName(), pluginList.FilePath, entries, missingPlugins);
    }

    /// <param name="listings">In load order; the masters are null when the plugin could not be read, and it carries no marker then.</param>
    /// <param name="pluginList">The entries of the game's plugin list in list order.</param>
    /// <param name="missingPlugins">The enabled entries of the plugin list whose file was not found.</param>
    /// <remarks>Masters are printed as the load order spells them, even if the plugin names them in another letter case.</remarks>
    internal static LoadOrderPlugins Create(
        IReadOnlyList<(PluginName Plugin, List<PluginName>? Masters, bool CarriesPatchMarker)> listings,
        PluginName patch,
        string pluginsFilePath,
        IReadOnlyList<PluginListEntry> pluginList,
        IEnumerable<PluginName> missingPlugins)
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
                [.. (listing.Masters ?? []).Select(table.WithKnownSpelling)])
            {
                CarriesPatchMarker = listing.CarriesPatchMarker,
            })],
            pluginsFilePath,
            new LoadOrderPositions(pluginList.Select(entry => entry.Plugin)),
            [.. pluginList.Where(entry => !entry.Enabled).Select(entry => entry.Plugin)],
            [.. missingPlugins]);
    }

    /// <returns>Null when the plugin could not be read.</returns>
    private static List<PluginName>? ReadMasters(ISkyrimModGetter? mod) =>
        mod?.MasterReferences.Select(master => master.Master.ToPluginName()).ToList();

    /// <returns>False when the plugin could not be read.</returns>
    private static bool ReadPatchMarker(ISkyrimModGetter? mod) => PatchMarker.IsIn(mod?.ModHeader.Description);
}
