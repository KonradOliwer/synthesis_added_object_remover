namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// What the load-order read collects beyond the target objects, every other placed object of the target spaces and the terrain of
/// the target worldspaces, and how it tells a moved replacement.
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
public sealed record WhatToCollect(bool Navmesh, bool OtherModDetails, bool TerrainVersions, bool TerrainLayers, bool Water, float MovedDistance);
