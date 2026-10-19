using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Compares the water of a cell the game shows with the water the target expected, and finds the plugin whose own edit made the
/// difference. Thread-safe.
/// </summary>
internal sealed class CellWaterComparison(TerrainPlugins plugins, ChangedTerrainSettings settings)
{
    /// <remarks>
    /// A worldspace copy without a default water height says nothing about water. A cell using that default has an unknown water
    /// height, and a cell whose expected or shown height is unknown is skipped; a plugin in between with an unknown height carries
    /// no change.
    /// </remarks>
    /// <returns>
    /// Null when the cell or its worldspace has no reference copy, when the expected or shown height is unknown, when the water the
    /// game shows differs from the expected water by no counted change, or when the plugin whose own edit gave the change does not count.
    /// </returns>
    public CellWaterChange? Compare(WaterCopies copies)
    {
        if (plugins.FromReference(copies.Cell, static copy => copy.Plugin) is not { } cellFromReference
            || plugins.FromReference(copies.Worldspace, static copy => copy.Plugin) is not { } worldspaceFromReference)
        {
            return null;
        }

        var versionsFromReference = JoinFromReference(cellFromReference, worldspaceFromReference);
        if (!versionsFromReference[0].IsKnown || !versionsFromReference[^1].IsKnown) return null;

        var (expected, shown) = (versionsFromReference[0].Level, versionsFromReference[^1].Level);
        var kind = FindChangeBetween(expected, shown);
        if (!IsCounted(kind)) return null;

        var origin = new ChangeOrigin(
            index => CarriesShownChange(versionsFromReference, index),
            index => plugins.CountsAsChanging(versionsFromReference[index].Plugin));
        var changer = versionsFromReference[origin.Find(versionsFromReference.Length - 1)].Plugin;
        return plugins.CountsAsChanging(changer) ? new CellWaterChange(expected, shown, plugins.PositionOf(changer), kind) : null;
    }

    /// <summary>
    /// The water the target expected first: the reference cell copy with the reference worldspace default, each chosen on its
    /// own, since a plugin's copy of the worldspace says nothing about the cell. Then the water after each later plugin with a
    /// copy of either, in load order, joining the last copies of both up to that plugin; the last version is the water the game
    /// shows. Both copy lists are already in load order, so they are merged rather than sorted.
    /// </summary>
    private WaterVersion[] JoinFromReference(CellWaterCopy[] cellFromReference, WorldspaceWaterCopy[] worldspaceFromReference)
    {
        var (cell, worldspace) = (cellFromReference[0], worldspaceFromReference[0]);
        var versions = new List<WaterVersion> { CreateVersion(cell.Plugin, cell, worldspace) };
        var (nextCell, nextWorldspace) = (1, 1);
        while (NextPlugin(cellFromReference, nextCell, worldspaceFromReference, nextWorldspace) is { } plugin)
        {
            for (; nextCell < cellFromReference.Length && cellFromReference[nextCell].Plugin == plugin; nextCell++) cell = cellFromReference[nextCell];
            for (; nextWorldspace < worldspaceFromReference.Length && worldspaceFromReference[nextWorldspace].Plugin == plugin; nextWorldspace++)
            {
                worldspace = worldspaceFromReference[nextWorldspace];
            }
            versions.Add(CreateVersion(plugin, cell, worldspace));
        }
        return [.. versions];
    }

    /// <returns>The earlier loading plugin of the next cell copy and the next worldspace copy; null when both lists are used up.</returns>
    private PluginName? NextPlugin(CellWaterCopy[] cells, int nextCell, WorldspaceWaterCopy[] worldspaces, int nextWorldspace)
    {
        PluginName? cellPlugin = nextCell < cells.Length ? cells[nextCell].Plugin : null;
        PluginName? worldspacePlugin = nextWorldspace < worldspaces.Length ? worldspaces[nextWorldspace].Plugin : null;
        if (cellPlugin is not { } fromCell) return worldspacePlugin;
        if (worldspacePlugin is not { } fromWorldspace) return fromCell;
        return plugins.PositionOf(fromCell) <= plugins.PositionOf(fromWorldspace) ? fromCell : fromWorldspace;
    }

    /// <summary>
    /// The game's rule: a cell without the "has water" flag has no water; with it, the cell's own height, else its worldspace's
    /// default. A cell using a default that the worldspace copy does not store has an unknown height.
    /// </summary>
    private static WaterVersion CreateVersion(PluginName plugin, CellWaterCopy cell, WorldspaceWaterCopy worldspace) =>
        new(
            plugin,
            cell.HasWater ? cell.OwnLevel ?? worldspace.DefaultLevel : null,
            !cell.HasWater || cell.OwnLevel != null || worldspace.DefaultLevel != null);

    /// <summary>A water height change counts only beyond the tolerance; water added or removed always differs.</summary>
    /// <returns><see cref="TerrainChangeKind.None"/> when the versions do not differ.</returns>
    private TerrainChangeKind FindChangeBetween(float? reference, float? version) => (reference, version) switch
    {
        (null, null) => TerrainChangeKind.None,
        (null, _) => TerrainChangeKind.WaterAdded,
        (_, null) => TerrainChangeKind.WaterRemoved,
        ({ } referenceLevel, { } versionLevel) =>
            MathF.Abs(versionLevel - referenceLevel) > settings.WaterHeightTolerance ? TerrainChangeKind.WaterHeight : TerrainChangeKind.None,
    };

    private bool IsCounted(TerrainChangeKind kind) => kind switch
    {
        TerrainChangeKind.WaterHeight => settings.CountWaterHeightChanges,
        TerrainChangeKind.WaterAdded or TerrainChangeKind.WaterRemoved => settings.CountWaterAddedOrRemoved,
        _ => false,
    };

    /// <summary>The version differs from the reference and matches the shown version, both beyond the tolerance.</summary>
    private bool CarriesShownChange(WaterVersion[] versionsFromReference, int index) =>
        versionsFromReference[index].IsKnown
        && FindChangeBetween(versionsFromReference[0].Level, versionsFromReference[index].Level) != TerrainChangeKind.None
        && FindChangeBetween(versionsFromReference[^1].Level, versionsFromReference[index].Level) == TerrainChangeKind.None;

    /// <summary>The cell's water as the load order stands after one plugin.</summary>
    /// <param name="Level">The height of the water surface; null where the cell has no water.</param>
    /// <param name="IsKnown">False where the cell uses a default water height that no copy stores.</param>
    private readonly record struct WaterVersion(PluginName Plugin, float? Level, bool IsKnown);
}
