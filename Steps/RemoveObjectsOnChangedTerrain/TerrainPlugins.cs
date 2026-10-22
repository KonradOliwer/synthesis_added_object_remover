using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What each plugin is to the terrain: the reference terrain's plugins, and the plugins that count as changing it.</summary>
internal sealed class TerrainPlugins(IdentifiedMods mods, LoadOrderPlugins loadOrder)
{
    private readonly LoadOrderPositions _positions = new(loadOrder.Listed);

    /// <summary>
    /// A cell's terrain versions, or its or its worldspace's water copies, from the reference on: the version the target was built
    /// on, which is the target's own version, else the last version among the target's masters. Only the versions loading after the reference can change it:
    /// the reference overrides the earlier ones.
    /// </summary>
    /// <param name="versions">The cell's versions in load order.</param>
    /// <returns>The reference first; null when neither the target nor one of its masters has a version of the cell.</returns>
    public TVersion[]? FromReference<TVersion>(TVersion[] versions, Func<TVersion, PluginName> pluginOf) =>
        ReferenceIn([.. versions.Select(pluginOf)]) is { } referenceIndex ? versions[referenceIndex..] : null;

    private int? ReferenceIn(PluginName[] versionPlugins) =>
        LastIndexIn(versionPlugins, plugin => plugin == mods.Target) ?? LastIndexIn(versionPlugins, mods.TargetMasters.Contains);

    /// <summary>
    /// Plugins that never count as clashing (the patch plugin among them, which holds earlier patchers' output) and plugins
    /// that require the target (its patches and other mods made for it) never count as changing the terrain.
    /// </summary>
    public bool CountsAsChanging(PluginName plugin) => !mods.IgnoredOrigins.Contains(plugin) && !mods.RequiringMods.Contains(plugin);

    /// <summary>The plugin's position in the load order: a plugin loading later has a higher position.</summary>
    public int PositionOf(PluginName plugin) => _positions.Of(plugin);

    /// <summary>The plugin at a load-order position, spelled as the load order lists it.</summary>
    public PluginName PluginAt(int position) => loadOrder.Listed[position].Mod;

    private static int? LastIndexIn(PluginName[] versionPlugins, Func<PluginName, bool> isInGroup)
    {
        var index = Array.FindLastIndex(versionPlugins, plugin => isInGroup(plugin));
        return index < 0 ? null : index;
    }
}
