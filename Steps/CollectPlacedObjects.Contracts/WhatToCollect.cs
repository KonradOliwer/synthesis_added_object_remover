namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What the load-order read collects beyond the target objects and the other mods' objects near them.</summary>
/// <param name="SupportOnlyObjects">Every other placed object of the target spaces, as possible supporters or objects around a marker.</param>
/// <param name="Terrain">The terrain of the target worldspaces.</param>
/// <param name="Navmesh">The winning navmeshes of the target spaces.</param>
/// <param name="OtherModDetails">The Editor IDs of other mods' objects, and the list of other mods' objects the target overrides.</param>
/// <param name="TerrainVersions">Every plugin's version of the terrain of the target worldspaces' cells.</param>
/// <param name="TerrainLayers">The texture layers of those versions; needs <paramref name="TerrainVersions"/>.</param>
/// <param name="Water">Every plugin's water of the target worldspaces' own exterior cells and of the worldspaces that supply their default water.</param>
public sealed record WhatToCollect(
    bool SupportOnlyObjects, bool Terrain, bool Navmesh, bool OtherModDetails, bool TerrainVersions, bool TerrainLayers, bool Water);
