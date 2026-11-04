using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <param name="OtherModsObjects">Whether other mods' placed objects the cleaned mod changed go back.</param>
/// <param name="Terrain">Whether heights, vertex colours and ground textures go back.</param>
/// <param name="Cells">Whether the cells' settings go back.</param>
/// <param name="Navmeshes">Whether navmeshes go back.</param>
/// <param name="ClearedArea">When the area around a change counts as cleared of the cleaned mod's objects, so the change goes back.</param>
/// <param name="TerrainEdgeBlendDistance">The distance over which restored ground is blended into ground that stays as the cleaned mod changed it.</param>
/// <param name="HeightChangeTolerance">The cleaned mod's changed-terrain height tolerance: heights closer than this count as equal.</param>
/// <param name="WaterHeightChangeTolerance">The cleaned mod's changed-terrain water height tolerance.</param>
/// <param name="TooClose">The too-close settings of the objects the cleaned mod changed, which judge a restored object against what stays.</param>
public sealed record WhereRemovedRestoreSettings(
    bool OtherModsObjects,
    bool Terrain,
    bool Cells,
    bool Navmeshes,
    ClearedArea ClearedArea,
    float TerrainEdgeBlendDistance,
    float HeightChangeTolerance,
    float WaterHeightChangeTolerance,
    TooCloseSettings TooClose)
{
    /// <summary>Whether any kind goes back; with all four switches off the step is switched off.</summary>
    public bool Runs => OtherModsObjects || Terrain || Cells || Navmeshes;

    /// <summary>
    /// Nothing when the step is off. Otherwise the navmeshes are always read. Other mods' objects that go back are judged at their old
    /// places and against every object that exists because of the cleaned mod, which is told from the world without the cleaned mod.
    /// </summary>
    public ScanNeeds Needs => Runs
        ? new ScanNeeds(
            Navmesh: true,
            JudgeOldPlaces: OtherModsObjects,
            DecideEveryObjectWithoutCleanedMod: OtherModsObjects,
            AreaRecordVersions: ReadAreaRecords())
        : new ScanNeeds();

    /// <summary>
    /// A kind whose switch is off stays as the cleaned mod made it, so it is still read when the guards of a kind that is on look at
    /// it: restored objects and navmeshes are kept off ground and out of water lines that stay changed, while the terrain guards look
    /// at no cell and the cell guards at no ground. Every kind's guards look at the navmeshes that stay changed, so those are always
    /// read.
    /// </summary>
    private AreaRecordReads ReadAreaRecords() => new(
        new AreaPartTolerances(HeightChangeTolerance, WaterHeightChangeTolerance),
        Cells: Cells || OtherModsObjects || Navmeshes,
        Lands: Terrain || OtherModsObjects || Navmeshes);
}
