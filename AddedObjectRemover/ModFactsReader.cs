using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>Reads the load order's plugin names, masters and whether each could be read; once per run, before the settings are checked.</summary>
internal static class ModFactsReader
{
    public static ModFacts Read(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        var listings = state.LoadOrder.ListedOrder
            .Select(listing => (listing.ModKey, Masters: ReadMasters(listing.Mod)))
            .ToList();
        var table = new ModTable(listings
            .Select(listing => listing.ModKey)
            .Append(state.PatchMod.ModKey)
            .Concat(listings.SelectMany(listing => listing.Masters ?? [])));
        return new ModFacts(
            table,
            table.RefOf(state.PatchMod.ModKey),
            [
                .. listings.Select(listing => new ModListing(
                    table.RefOf(listing.ModKey),
                    listing.Masters != null,
                    [.. (listing.Masters ?? []).Select(table.RefOf)])),
            ]);
    }

    /// <returns>Null when the plugin could not be read.</returns>
    private static List<ModKey>? ReadMasters(ISkyrimModGetter? mod) =>
        mod?.MasterReferences.Select(master => master.Master).ToList();
}
