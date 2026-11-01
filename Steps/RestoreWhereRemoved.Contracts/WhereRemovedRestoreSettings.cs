using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <param name="SearchRadius">How far, horizontally, around a judged point the cleaned mod's objects count as its area.</param>
/// <param name="MaxStandingPercent">The share of the area's weight, in percent (0.1 is a tenth of a percent), that may still stand for the area to count as cleared.</param>
public sealed record ClearedArea(float SearchRadius, double MaxStandingPercent, ClearedAreaMeasure Measure);

/// <param name="OtherModsObjects">Whether other mods' placed objects the cleaned mod changed go back.</param>
/// <param name="Terrain">Whether heights, vertex colours and ground textures go back.</param>
/// <param name="Cells">Whether the cells' settings go back.</param>
/// <param name="Navmeshes">Whether navmeshes go back.</param>
/// <param name="ClearedArea">When the area around a change counts as cleared of the cleaned mod's objects, so the change goes back.</param>
/// <param name="TerrainEdgeBlendDistance">The distance over which restored ground is blended into ground that stays as the cleaned mod changed it.</param>
/// <param name="HeightChangeTolerance">The cleaned mod's changed-terrain height tolerance: heights closer than this count as equal.</param>
/// <param name="WaterHeightChangeTolerance">The cleaned mod's changed-terrain water height tolerance.</param>
/// <param name="TooClose">The too-close settings of the objects the cleaned mod changed, which judge a restored object against what stays.</param>
/// <param name="ChangedTerrain">The changed-terrain settings of the objects the cleaned mod changed.</param>
public sealed record WhereRemovedRestoreSettings(
    bool OtherModsObjects,
    bool Terrain,
    bool Cells,
    bool Navmeshes,
    ClearedArea ClearedArea,
    float TerrainEdgeBlendDistance,
    float HeightChangeTolerance,
    float WaterHeightChangeTolerance,
    TooCloseSettings TooClose,
    ChangedTerrainSettings ChangedTerrain);
