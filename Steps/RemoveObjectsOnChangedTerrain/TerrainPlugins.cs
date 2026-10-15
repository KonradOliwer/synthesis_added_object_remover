using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What each plugin is to the terrain: the reference terrain's groups, and the plugins that count as changing it.</summary>
internal sealed class TerrainPlugins(IdentifiedMods mods, LoadOrderPlugins loadOrder)
{
    /// <summary>
    /// The reference terrain among a cell's versions (in load order): the last dependant's, else the target's, else the last
    /// base-game plugin's. The last dependant is the one the game shows among them, and a patch or add-on loads after what it builds on.
    /// </summary>
    /// <returns>Null when none of these groups has a version of the cell.</returns>
    public int? ReferenceIn(TerrainVersion[] versions) =>
        LastIndexIn(versions, mods.Dependants.Contains)
        ?? LastIndexIn(versions, plugin => plugin == mods.Target)
        ?? LastIndexIn(versions, mods.BaseGame.Contains);

    /// <summary>
    /// Plugins of the reference groups never change the reference, and plugins that never count as clashing (the patch plugin
    /// among them, which holds earlier patchers' output) never count as changing it either.
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
