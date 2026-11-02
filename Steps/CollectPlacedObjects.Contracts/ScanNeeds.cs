namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// What the load-order read collects beyond the target objects. Each step's settings state their own; <see cref="UnionOf"/> gives what the run reads.
/// </summary>
/// <param name="Navmesh">The winning navmeshes of the target spaces.</param>
/// <param name="OtherModDetails">The Editor IDs of other mods' objects, and the list of other mods' objects the target overrides.</param>
/// <param name="TerrainVersions">Every plugin's version of the terrain of the target worldspaces' cells.</param>
/// <param name="TerrainLayers">The texture layers of those versions; needs <paramref name="TerrainVersions"/>.</param>
/// <param name="Water">Every plugin's water of the target worldspaces' own exterior cells and of the worldspaces that supply their default water.</param>
/// <param name="TerrainForChangedObjectsOnly">
/// Only the checks of the changed objects ask for the terrain, water and layer reads above, so the scan skips them when the cleaned
/// mod overrides no placed record of another plugin, which means it has no changed object.
/// </param>
/// <param name="JudgeOldPlaces">
/// Whether an earlier version of a changed object is judged at its old place, which needs the world without the cleaned mod: the
/// walk goes below the plugins that need the cleaned mod, and, when the scan finds a changed object, every other-mod and
/// support-only object is also decided in that world. Off, the scan does neither.
/// </param>
/// <param name="DecideEveryObjectWithoutCleanedMod">
/// With <paramref name="JudgeOldPlaces"/>, every other-mod and support-only object is decided in the world without the cleaned mod
/// even when the scan finds no changed object: other mods' objects that go back where the cleaned mod was removed are checked
/// against every object that exists because of the cleaned mod.
/// </param>
/// <param name="AreaRecordVersions">
/// Null unless restoring where the cleaned mod's objects were removed is on. On, the cell and LAND records of other plugins the
/// cleaned mod changed are read and judged as far as asked for, so are the navmeshes it overrides, deletes or adds, and the links
/// into the records and cells the cleaned mod changed are read, to tell which of them the cleaned mod's own records link to.
/// </param>
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
    /// <param name="needs">The needs of each step that is on; a step that is off gives null or nothing.</param>
    public static ScanNeeds UnionOf(params ReadOnlySpan<ScanNeeds?> needs) =>
        needs.ToArray().OfType<ScanNeeds>().Aggregate(new ScanNeeds(), (union, step) => union.Union(step));

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
