namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// What the load-order read collects beyond the target objects, every other placed object of the target spaces and the terrain of
/// the target worldspaces, and how it tells the other plugins' objects the target plugin changed.
/// </summary>
/// <param name="Needs">What the steps that are on need read.</param>
/// <param name="MovedDistance">
/// How far, in game units, a record the target overrides must stand from the version it replaces to count as moved and be judged;
/// at most this far it stands where it stood.
/// </param>
/// <param name="ProcessReplaced">
/// Whether a record the target overrides within <paramref name="MovedDistance"/> with another base object is also judged; otherwise
/// it is skipped like any override that leaves the object where it stood.
/// </param>
public sealed record WhatToCollect(ScanNeeds Needs, float MovedDistance, bool ProcessReplaced);
