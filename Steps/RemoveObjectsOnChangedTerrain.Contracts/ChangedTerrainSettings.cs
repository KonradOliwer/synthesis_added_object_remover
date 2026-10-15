namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

/// <param name="On">Whether the step runs; when off it reads no terrain and proposes nothing.</param>
/// <param name="CountHeightChanges">Whether a change of terrain height counts as a change.</param>
/// <param name="HeightTolerance">A height change up to this many game units is ignored; 0 counts every change.</param>
/// <param name="CountTerrainTypeChanges">Whether a change of terrain type counts as a change; a different texture of the same type does not.</param>
/// <param name="DistanceInUnits">How far around the object the terrain is checked, in game units, for <see cref="TerrainDistanceMode.GameUnits"/>.</param>
/// <param name="DistanceSizeMultiplier">How far around the object the terrain is checked, as a multiple of its size, for <see cref="TerrainDistanceMode.ObjectSize"/>.</param>
public sealed record ChangedTerrainSettings(
    bool On,
    TerrainCheckedObjects Objects,
    bool CountHeightChanges,
    float HeightTolerance,
    bool CountTerrainTypeChanges,
    TerrainDistanceMode DistanceMode,
    float DistanceInUnits,
    float DistanceSizeMultiplier);
