namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <summary>
/// The terrain area of an object is checked by each area that is on, size area first; the object is found when any of them
/// holds a change, as the too-close step combines its two removal zones.
/// </summary>
/// <param name="On">Whether the step runs; when off it reads no terrain and proposes nothing.</param>
/// <param name="CountHeightChanges">Whether a change of terrain height counts as a change.</param>
/// <param name="HeightTolerance">A height change up to this many game units is ignored; 0 counts every change.</param>
/// <param name="CountTerrainTypeChanges">Whether a change of terrain type counts as a change; a different texture of the same type does not.</param>
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
    bool AreaBySize,
    float AreaMultiplier,
    bool AreaByDistance,
    float AreaDistanceInUnits);
