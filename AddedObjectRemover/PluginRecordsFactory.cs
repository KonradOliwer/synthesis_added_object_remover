using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the Synthesis state's load order, link cache and patch plugin.</summary>
internal sealed class PluginRecordsFactory(
    ILoadOrder<IModListing<ISkyrimModGetter>> loadOrder,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    ISkyrimMod patchMod) : IPluginRecordsFactory
{
    public IPluginRecords Open() => new PluginRecords(loadOrder, linkCache, patchMod);
}
