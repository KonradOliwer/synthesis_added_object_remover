using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>What changed in the terrain under or near a target object; a removal can name several.</summary>
[Flags]
public enum TerrainChangeKind
{
    None = 0,
    Height = 1,
    TerrainType = 2,
    WaterHeight = 4,
    WaterAdded = 8,
    WaterRemoved = 16,
}

/// <summary>The changed-terrain step's reason: the terrain or water under or near the object changed.</summary>
/// <param name="ChangedBy">
/// The plugin whose terrain or water change the game shows; null when only the patch holds it: an earlier patcher of the group
/// made it.
/// </param>
public sealed record TerrainChanged(PluginName? ChangedBy, TerrainChangeKind Kind) : RemovalReason;
