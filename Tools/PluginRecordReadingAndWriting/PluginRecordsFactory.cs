using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the Synthesis state's load order, link cache and patch plugin, and the run's known plugin names.</summary>
/// <param name="workers">How many threads may read records at once.</param>
public sealed class PluginRecordsFactory(
    ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    ISkyrimMod patchMod,
    KnownPluginNames knownPlugins,
    int workers) : IPluginRecordsFactory
{
    public IPluginRecords Open() => new PluginRecords(loadOrder, linkCache, patchMod, new PluginNames(knownPlugins), new Execution(workers));
}
