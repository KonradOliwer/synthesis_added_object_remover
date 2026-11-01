namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// What one step needs the load-order read to collect. Each step's settings state their own; <see cref="UnionOf"/> gives what the run reads.
/// </summary>
/// <param name="Navmesh">See <see cref="WhatToCollect.Navmesh"/>.</param>
/// <param name="OtherModDetails">See <see cref="WhatToCollect.OtherModDetails"/>.</param>
/// <param name="TerrainVersions">See <see cref="WhatToCollect.TerrainVersions"/>.</param>
/// <param name="TerrainLayers">See <see cref="WhatToCollect.TerrainLayers"/>.</param>
/// <param name="Water">See <see cref="WhatToCollect.Water"/>.</param>
/// <param name="TerrainForChangedObjectsOnly">See <see cref="WhatToCollect.TerrainForChangedObjectsOnly"/>.</param>
/// <param name="JudgeOldPlaces">See <see cref="WhatToCollect.JudgeOldPlaces"/>.</param>
/// <param name="DecideEveryObjectWithoutCleanedMod">See <see cref="WhatToCollect.DecideEveryObjectWithoutCleanedMod"/>.</param>
/// <param name="AreaRecordVersions">See <see cref="WhatToCollect.AreaRecordVersions"/>.</param>
public sealed record ScanNeeds(
    bool Navmesh = false,
    bool OtherModDetails = false,
    bool TerrainVersions = false,
    bool TerrainLayers = false,
    bool Water = false,
    bool TerrainForChangedObjectsOnly = false,
    bool JudgeOldPlaces = false,
    bool DecideEveryObjectWithoutCleanedMod = false,
    AreaRecordReads? AreaRecordVersions = null)
{
    /// <param name="needs">The needs of each step that is on; a step that is off gives null.</param>
    public static ScanNeeds UnionOf(params ReadOnlySpan<ScanNeeds?> needs)
    {
        var union = new ScanNeeds();
        foreach (var step in needs)
        {
            if (step != null) union = union.Union(step);
        }

        return union;
    }

    private ScanNeeds Union(ScanNeeds other) => new(
        Navmesh || other.Navmesh,
        OtherModDetails || other.OtherModDetails,
        TerrainVersions || other.TerrainVersions,
        TerrainLayers || other.TerrainLayers,
        Water || other.Water,
        TerrainForChangedObjectsOnly || other.TerrainForChangedObjectsOnly,
        JudgeOldPlaces || other.JudgeOldPlaces,
        DecideEveryObjectWithoutCleanedMod || other.DecideEveryObjectWithoutCleanedMod,
        UnionOfAreaRecordVersions(other.AreaRecordVersions));

    /// <summary>The tolerances of two reads cannot be merged, so only one step may ask for them.</summary>
    private AreaRecordReads? UnionOfAreaRecordVersions(AreaRecordReads? other) =>
        (AreaRecordVersions, other) switch
        {
            (null, _) => other,
            (_, null) => AreaRecordVersions,
            _ => throw new InvalidOperationException("Two steps ask for the area record reads."),
        };
}
