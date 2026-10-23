using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestLoadOrderPlugins
{
    /// <summary>Plugins whose game plugin list is the listed plugins themselves, all enabled, read from a file of a fixed name.</summary>
    public static LoadOrderPlugins Of(KnownPluginNames mods, PluginName patch, ImmutableArray<PluginListing> listed) =>
        new(mods, patch, listed, "Plugins.txt", new LoadOrderPositions(listed), [], []);

    /// <summary>Plugins with a game plugin list of their own, read from a file of a fixed name; the disabled entries are named separately.</summary>
    public static LoadOrderPlugins Of(
        KnownPluginNames mods, PluginName patch, ImmutableArray<PluginListing> listed, IEnumerable<PluginName> pluginList, IEnumerable<PluginName> disabled) =>
        new(mods, patch, listed, "Plugins.txt", new LoadOrderPositions(pluginList), [.. disabled], []);
}
