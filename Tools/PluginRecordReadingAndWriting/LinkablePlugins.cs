using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Which plugins a plugin's records can link to: a FormKey names a record of the plugin itself or of one of its masters.</summary>
internal static class LinkablePlugins
{
    public static HashSet<PluginName> Of(ISkyrimModGetter mod, PluginNames names) =>
        mod.MasterReferences.Select(master => names.Of(master.Master)).Append(names.Of(mod.ModKey)).ToHashSet();

    /// <summary>The patch counts for every plugin: it is read for what earlier patchers in the group wrote into it.</summary>
    public static bool MayLinkToAny(ISkyrimModGetter mod, IReadOnlySet<PluginName> plugins, ModKey patch, PluginNames names) =>
        mod.ModKey == patch || Of(mod, names).Overlaps(plugins);
}
