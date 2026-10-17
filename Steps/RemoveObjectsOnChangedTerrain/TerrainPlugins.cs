using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What each plugin is to the terrain: the reference terrain's plugins, and the plugins that count as changing it.</summary>
internal sealed class TerrainPlugins(IdentifiedMods mods, LoadOrderPlugins loadOrder)
{
    /// <summary>
    /// The reference terrain among a cell's versions (in load order): the terrain the target was built on, which is the target's
    /// own version, else the last version among the target's masters.
    /// </summary>
    /// <returns>Null when neither the target nor one of its masters has a version of the cell.</returns>
    public int? ReferenceIn(TerrainVersion[] versions) =>
        LastIndexIn(versions, plugin => plugin == mods.Target) ?? LastIndexIn(versions, mods.TargetMasters.Contains);

    /// <summary>
    /// Plugins that never count as clashing (the patch plugin among them, which holds earlier patchers' output) and plugins
    /// that require the target (its patches and add-ons) never count as changing the terrain.
    /// </summary>
    public bool CountsAsChanging(PluginName plugin) => !mods.IgnoredOrigins.Contains(plugin) && !mods.Dependants.Contains(plugin);

    /// <summary>The plugin's position in the load order: a plugin loading later has a higher position.</summary>
    public int PositionOf(PluginName plugin) =>
        loadOrder.Listed.Select((listing, position) => (listing.Mod, Position: position)).Last(listing => listing.Mod == plugin).Position;

    /// <summary>The plugin at a load-order position, spelled as the load order lists it.</summary>
    public PluginName PluginAt(int position) => loadOrder.Listed[position].Mod;

    private static int? LastIndexIn(TerrainVersion[] versions, Func<PluginName, bool> isInGroup)
    {
        var index = Array.FindLastIndex(versions, version => isInGroup(version.Plugin));
        return index < 0 ? null : index;
    }
}
