namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <param name="LookAround">Largest distance from a visible target object to the target's other visible objects that count as its surroundings.</param>
/// <param name="MinOccupiedDirections">Count of the 8 directions (1-8) that must be occupied for the direction rule to apply.</param>
/// <param name="DirectionClearedPercent">Removed share of a direction's ground area (10-100) that makes the direction cleared.</param>
/// <param name="ClearedDirectionsPercent">Share of the occupied directions (10-100) that must be cleared for removal.</param>
/// <param name="NeverRemove">Visible object kinds that are never removed when left behind.</param>
public sealed record VisibleLeftBehindSettings(
    float LookAround,
    int MinOccupiedDirections,
    int DirectionClearedPercent,
    int ClearedDirectionsPercent,
    IReadOnlySet<VisibleObjectKind> NeverRemove);
