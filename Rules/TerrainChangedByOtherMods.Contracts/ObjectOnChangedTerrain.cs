using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>A target object on terrain that another mod changed.</summary>
/// <param name="ChangedBy">
/// The later loading of the plugins named for the counted changes around the object; null when that is the patch, whose change an
/// earlier patcher of the group made.
/// </param>
/// <param name="Kind">How the terrain and water the game shows differ from the accepted ones, at the vertices of the counted changes.</param>
public readonly record struct ObjectOnChangedTerrain(TargetId Target, PluginName? ChangedBy, TerrainChangeKind Kind);
