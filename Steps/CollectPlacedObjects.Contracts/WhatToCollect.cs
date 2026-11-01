namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// What the load-order read collects beyond the target objects, every other placed object of the target spaces and the terrain of
/// the target worldspaces, and how it tells the other plugins' objects the target plugin changed.
/// </summary>
/// <param name="Navmesh">The winning navmeshes of the target spaces.</param>
/// <param name="OtherModDetails">The Editor IDs of other mods' objects, and the list of other mods' objects the target overrides.</param>
/// <param name="TerrainVersions">Every plugin's version of the terrain of the target worldspaces' cells.</param>
/// <param name="TerrainLayers">The texture layers of those versions; needs <paramref name="TerrainVersions"/>.</param>
/// <param name="Water">Every plugin's water of the target worldspaces' own exterior cells and of the worldspaces that supply their default water.</param>
/// <param name="MovedDistance">
/// How far, in game units, a record the target overrides must stand from the version it replaces to count as moved and be judged;
/// at most this far it stands where it stood.
/// </param>
/// <param name="ProcessReplaced">
/// Whether a record the target overrides within <paramref name="MovedDistance"/> with another base object is also judged; otherwise
/// it is skipped like any override that leaves the object where it stood.
/// </param>
/// <param name="JudgeOldPlaces">
/// Whether an earlier version of a changed object is judged at its old place, which needs the world without the cleaned mod: the
/// walk goes below the plugins that need the cleaned mod, and, when the scan finds a changed object, every other-mod and
/// support-only object is also decided in that world. Off, the scan does neither.
/// </param>
/// <param name="TerrainForChangedObjectsOnly">
/// Only the checks of the changed objects ask for the terrain, water and layer reads above, so the scan skips them when the cleaned
/// mod overrides no placed record of another plugin, which means it has no changed object.
/// </param>
/// <param name="AreaRecordVersions">
/// Null unless restoring where the cleaned mod's objects were removed is on. On, the cell and LAND records of other plugins the
/// cleaned mod changed are read and judged as far as asked for, so are the navmeshes it overrides, deletes or adds, and the links
/// into the records and cells the cleaned mod changed are read, to tell which of them the cleaned mod's own records link to.
/// </param>
/// <param name="DecideEveryObjectWithoutCleanedMod">
/// With <paramref name="JudgeOldPlaces"/>, every other-mod and support-only object is decided in the world without the cleaned mod
/// even when the scan finds no changed object: other mods' objects that go back where the cleaned mod was removed are checked
/// against every object that exists because of the cleaned mod.
/// </param>
public sealed record WhatToCollect(
    bool Navmesh,
    bool OtherModDetails,
    bool TerrainVersions,
    bool TerrainLayers,
    bool Water,
    float MovedDistance,
    bool ProcessReplaced,
    bool JudgeOldPlaces,
    bool TerrainForChangedObjectsOnly,
    AreaRecordReads? AreaRecordVersions = null,
    bool DecideEveryObjectWithoutCleanedMod = false)
{
    /// <summary>What the steps need read, with the moved distance and the replaced setting of the changed-object judging.</summary>
    public static WhatToCollect Of(ScanNeeds needs, float movedDistance, bool processReplaced) => new(
        needs.Navmesh,
        needs.OtherModDetails,
        needs.TerrainVersions,
        needs.TerrainLayers,
        needs.Water,
        movedDistance,
        processReplaced,
        needs.JudgeOldPlaces,
        needs.TerrainForChangedObjectsOnly,
        needs.AreaRecordVersions,
        needs.DecideEveryObjectWithoutCleanedMod);
}
