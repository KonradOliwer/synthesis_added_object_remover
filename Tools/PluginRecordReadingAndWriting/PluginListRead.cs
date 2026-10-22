using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins.Order;

namespace AddedObjectRemover;

/// <summary>The game's plugin list as read, or the problem that makes it unusable.</summary>
public abstract record PluginListRead
{
    /// <param name="FilePath">The plugins file the list was read from.</param>
    /// <param name="Listings">Every entry in list order, enabled or not.</param>
    public sealed record Found(string FilePath, ImmutableArray<ILoadOrderListingGetter> Listings) : PluginListRead;

    public sealed record Failed(PluginListProblem Problem) : PluginListRead;
}
