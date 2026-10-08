using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <param name="LookAround">Largest distance from an invisible target object to the target's visible objects that count as its surroundings.</param>
/// <param name="DirectionClearedPercent">Removed share of a direction's ground area (10-100) that makes the direction cleared.</param>
/// <param name="ClearedDirectionsPercent">Share of the occupied directions (10-100) that must be cleared for removal.</param>
/// <param name="OccupiedDirectionsPercent">Share of all directions (10-100) that must be occupied for the direction rule to apply.</param>
/// <param name="NeverRemove">Invisible object kinds that are never removed when left behind.</param>
/// <param name="Preset">The protected-types choice the kinds were resolved from, for the settings echo.</param>
public sealed record LeftBehindOptions(
    float LookAround,
    int DirectionClearedPercent,
    int ClearedDirectionsPercent,
    int OccupiedDirectionsPercent,
    IReadOnlySet<InvisibleObjectKind> NeverRemove,
    ProtectedInvisibleObjectsPreset Preset);
