using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Opens the listed plugins from the data folder, in list order and with their enabled flags. A plugin whose file is missing
/// gets a listing without a mod. Disposing the result closes the plugins it opened.
/// </summary>
public delegate ILoadOrder<IModListing<ISkyrimModGetter>> PluginOpener(DirectoryPath dataFolder, IReadOnlyList<ILoadOrderListingGetter> listings);
