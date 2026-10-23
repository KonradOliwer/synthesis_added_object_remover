using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the final world; each run's reader gets a link cache of its own over the world's order.</summary>
/// <param name="workers">How many threads may read records at once.</param>
public sealed class PluginRecordsFactory(FinalWorld world, int workers) : IPluginRecordsFactory
{
    public IPluginRecords Open() =>
        new PluginRecords(
            world.Order,
            new RunLinkCache(world.Order.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>()),
            world.Patch,
            new PluginNames(world.Plugins.Mods),
            new Execution(workers));
}
