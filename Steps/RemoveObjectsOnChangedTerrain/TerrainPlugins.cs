namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>What each plugin is to the terrain and the water: how far its versions are believed in the accepted world, and where it loads.</summary>
internal sealed class TerrainPlugins(AcceptedWorld accepted, LoadOrderPlugins loadOrder)
{
    /// <summary>
    /// The position a change only the patch holds is named at: below every plugin, so that the later loading of several changers is
    /// always a real mod when one changed something, and the patch is named only when it alone holds every change.
    /// </summary>
    public const int PatchOnlyChange = -1;

    private readonly LoadOrderPositions _positions = new(loadOrder.Listed);

    /// <summary>How far the plugin's version of a land record, a cell or a worldspace is believed.</summary>
    public VersionTrust TrustOf(PluginName plugin) => accepted.TrustOf(plugin);

    /// <summary>The plugin's position in the load order: a plugin loading later has a higher position.</summary>
    public int PositionOf(PluginName plugin) => _positions.Of(plugin);

    /// <summary>The plugin named for a change made at a load-order position, spelled as the load order lists it.</summary>
    /// <returns>Null for <see cref="PatchOnlyChange"/>: an earlier patcher of the group made the change, which is named instead.</returns>
    public PluginName? ChangerAt(int position) => position == PatchOnlyChange ? null : loadOrder.Listed[position].Mod;
}
