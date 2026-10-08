namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What the load-order read collects beyond the target objects and the other mods' objects near them.</summary>
/// <param name="SupportOnlyObjects">Every other placed object of the target spaces, as possible supporters or objects around a marker.</param>
/// <param name="Terrain">The terrain of the target worldspaces.</param>
/// <param name="Navmesh">The winning navmeshes of the target spaces.</param>
/// <param name="OtherEditorIds">The Editor IDs of other mods' objects.</param>
/// <param name="OverriddenOthersList">The list of other mods' objects the target overrides.</param>
public sealed record WhatToCollect(bool SupportOnlyObjects, bool Terrain, bool Navmesh, bool OtherEditorIds, bool OverriddenOthersList);
