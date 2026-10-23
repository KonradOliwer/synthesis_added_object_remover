using System.Collections.Immutable;
using System.IO.Abstractions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Records.DI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Builds the final world from the Synthesis state's plugins, the patch being written and the game's plugin list. The patch sits
/// at the position of its entry in the list (the end when it has none); only the enabled entries after it are opened.
/// </summary>
public sealed class FinalWorldFactory(PluginOpener openPlugins)
{
    /// <summary>
    /// Opens the plugins as lazy read-only views, as Synthesis opens its own. Synthesis does not hand its language and encoding
    /// arguments to the patcher but sets the default language, so localized strings are read in that language with the
    /// library's default encodings.
    /// </summary>
    public static PluginOpener ImportFromDataFolder(GameRelease release) =>
        (dataFolder, listings) => Import(release, dataFolder, listings);

    /// <param name="stateListings">
    /// The Synthesis state's load order: the plugins above the patch, then the patch. They are reused as they are, never changed
    /// or disposed.
    /// </param>
    /// <param name="patch">The patch being written; it replaces any old copy of itself on disk.</param>
    public FinalWorld Create(
        IEnumerable<IModListing<ISkyrimModGetter>> stateListings, ISkyrimModGetter patch, PluginListRead.Found pluginList, DirectoryPath dataFolder)
    {
        var opened = openPlugins(dataFolder, EnabledEntriesBelow(patch.ModKey, pluginList));
        var order = new LoadOrder<IModListing<ISkyrimModGetter>>(
            stateListings
                .Where(listing => listing.ModKey != patch.ModKey)
                .Append(new ModListing<ISkyrimModGetter>(patch, enabled: true))
                .Concat(opened.ListedOrder.Where(listing => listing.Mod != null)),
            disposeItems: false);
        return new FinalWorld(order, patch.ModKey, LoadOrderPluginsReader.Read(order, patch.ModKey, pluginList, MissingPlugins(opened)), opened);
    }

    /// <remarks>The patch's own entry is never opened, enabled or not: an old copy of the patch on disk is not part of the final world.</remarks>
    private static List<ILoadOrderListingGetter> EnabledEntriesBelow(ModKey patch, PluginListRead.Found pluginList) =>
        [.. pluginList.Listings.SkipWhile(listing => listing.ModKey != patch).Skip(1).Where(listing => listing.Enabled)];

    private static ImmutableArray<PluginName> MissingPlugins(ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> opened) =>
        [.. opened.ListedOrder.Where(listing => listing.Mod == null).Select(listing => listing.ModKey.ToPluginName())];

    private static ILoadOrder<IModListing<ISkyrimModGetter>> Import(
        GameRelease release, DirectoryPath dataFolder, IReadOnlyList<ILoadOrderListingGetter> listings)
    {
        var fileSystem = new FileSystem();
        var game = new GameReleaseInjection(release);
        var folder = new DataDirectoryInjection(dataFolder);
        var importer = new LoadOrderImporter<ISkyrimModGetter>(
            fileSystem,
            folder,
            new LoadOrderListingsInjection(listings),
            new ModImporter<ISkyrimModGetter>(fileSystem, game),
            new MasterFlagsLookupProvider(game, fileSystem, folder));
        return importer.Import(new BinaryReadParameters
        {
            StringsParam = new StringsReadParameters { TargetLanguage = TranslatedString.DefaultLanguage },
            FileSystem = fileSystem,
        });
    }
}
