using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// The plugins the game loads: the plugins Synthesis loaded above the patch, the patch being written at its place in the game's
/// plugin list, then the enabled plugins below it. Synthesis owns the plugins above the patch and the patch itself; disposing
/// closes only the plugins below the patch, which the factory opened.
/// </summary>
public sealed class FinalWorld : IDisposable
{
    private readonly IDisposable _openedPlugins;

    internal FinalWorld(
        ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> order,
        ModKey patch,
        LoadOrderPlugins plugins,
        IDisposable openedPlugins)
    {
        Order = order;
        Patch = patch;
        Plugins = plugins;
        _openedPlugins = openedPlugins;
    }

    /// <summary>Lowest priority first; below the patch it holds only enabled plugins whose file exists.</summary>
    internal ILoadOrderGetter<IModListingGetter<ISkyrimModGetter>> Order { get; }

    internal ModKey Patch { get; }

    /// <summary>The plugins of <see cref="Order"/>, lowest priority first.</summary>
    public IReadOnlyList<PluginName> ListedPlugins => [.. Order.ListedOrder.Select(listing => listing.ModKey.ToPluginName())];

    public LoadOrderPlugins Plugins { get; }

    public void Dispose() => _openedPlugins.Dispose();
}
