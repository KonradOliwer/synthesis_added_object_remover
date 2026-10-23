using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What each plugin is to the terrain and the water: how far its versions are believed, and where it loads.</summary>
internal sealed class TerrainPlugins(ModTrustTable trust, LoadOrderPlugins loadOrder)
{
    private readonly LoadOrderPositions _positions = new(loadOrder.Listed);

    /// <summary>How far the plugin's version of a land record, a cell or a worldspace is believed.</summary>
    public VersionTrust TrustOf(PluginName plugin) => trust.VersionTrustOf(plugin);

    /// <summary>The plugin's position in the load order: a plugin loading later has a higher position.</summary>
    public int PositionOf(PluginName plugin) => _positions.Of(plugin);

    /// <summary>The plugin named for a change made at a load-order position, spelled as the load order lists it.</summary>
    /// <returns>Null for the patch: a change only it holds was made by an earlier patcher of the group, which is named instead.</returns>
    public PluginName? ChangerAt(int position)
    {
        var plugin = loadOrder.Listed[position].Mod;
        return trust.IsPatch(plugin) ? null : plugin;
    }
}
