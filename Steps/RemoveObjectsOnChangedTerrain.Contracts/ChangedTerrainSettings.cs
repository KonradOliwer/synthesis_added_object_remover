using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>
/// The terrain area of an object is checked by each area that is on, size area first; the object is found when any of them
/// holds a change, as the too-close step combines its two removal zones.
/// </summary>
/// <param name="On">Whether the step runs; when off it reads no terrain and proposes nothing.</param>
/// <param name="CountHeightChanges">Whether a change of terrain height counts as a change.</param>
/// <param name="HeightTolerance">A height change up to this many game units is ignored; 0 counts every change.</param>
/// <param name="CountTerrainTypeChanges">Whether a change of terrain type counts as a change; a different texture of the same type does not.</param>
/// <param name="MinTerrainTypeChangePercent">
/// The share (0-100) of an area's vertices whose terrain type must have changed for the type change to count; 0 counts any one
/// changed vertex.
/// </param>
/// <param name="CountWaterHeightChanges">Whether a change of a cell's water height counts as a change.</param>
/// <param name="WaterHeightTolerance">A water height change up to this many game units is ignored; 0 counts every change.</param>
/// <param name="CountWaterAddedOrRemoved">Whether water added to or removed from a cell counts as a change.</param>
/// <param name="MinWaterCoverChangePercent">
/// The share (0-100) of an area's vertices that the counted water changes must have covered or uncovered for the water change to
/// count; 0 counts any one such vertex.
/// </param>
/// <param name="AreaBySize">Whether the area grown by <paramref name="AreaMultiplier"/> is on.</param>
/// <param name="AreaMultiplier">How far the area reaches around the object, as a multiple of its size.</param>
/// <param name="AreaByDistance">Whether the area grown by <paramref name="AreaDistanceInUnits"/> is on.</param>
/// <param name="AreaDistanceInUnits">How far the area reaches around the object, in game units.</param>
public sealed record ChangedTerrainSettings(
    bool On,
    TerrainCheckedObjects Objects,
    bool CountHeightChanges,
    float HeightTolerance,
    bool CountTerrainTypeChanges,
    int MinTerrainTypeChangePercent,
    bool CountWaterHeightChanges,
    float WaterHeightTolerance,
    bool CountWaterAddedOrRemoved,
    int MinWaterCoverChangePercent,
    bool AreaBySize,
    float AreaMultiplier,
    bool AreaByDistance,
    float AreaDistanceInUnits)
{
    public bool CountsWaterChanges => CountWaterHeightChanges || CountWaterAddedOrRemoved;

    /// <summary>Every plugin's terrain is read only when the step runs, its texture layers only when it counts terrain types, and its water only when it counts a water change.</summary>
    public ScanNeeds Needs => new(TerrainVersions: On, TerrainLayers: On && CountTerrainTypeChanges, Water: On && CountsWaterChanges);

    /// <summary>
    /// When only the changed objects' check is on, the scan reads terrain only if the cleaned mod overrides a record of another plugin;
    /// whether an override is a changed object is known only after the scan.
    /// </summary>
    /// <param name="cleanedMod">The settings of the cleaned mod's own objects.</param>
    /// <param name="changedObjects">The settings of the objects the cleaned mod changed.</param>
    public static ScanNeeds NeedsOfBoth(ChangedTerrainSettings cleanedMod, ChangedTerrainSettings changedObjects) =>
        ScanNeeds.UnionOf(cleanedMod.Needs, changedObjects.Needs) with { TerrainForChangedObjectsOnly = !cleanedMod.On && changedObjects.On };
}
