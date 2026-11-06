namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>
/// The terrain area of an object is checked by each area that is on, size area first; the object is found when any of them
/// holds a change, as the too-close rule combines its two removal zones.
/// </summary>
/// <param name="On">Whether the user switched the check on; <see cref="Runs"/> says whether it can find anything.</param>
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

    public bool CountsAChange => CountHeightChanges || CountTerrainTypeChanges || CountsWaterChanges;

    public bool HasAnArea => AreaBySize || AreaByDistance;

    /// <summary>On, and it counts a change in at least one area; otherwise it could find nothing, so it reads no terrain and proposes nothing.</summary>
    public bool Runs => On && CountsAChange && HasAnArea;

    /// <summary>The settings with the switch as the rule runs, so two groups that both find nothing are equal whatever their switch.</summary>
    public ChangedTerrainSettings AsRun => this with { On = Runs };
}
