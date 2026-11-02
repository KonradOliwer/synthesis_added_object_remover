namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>The settings of the surroundings rule a left-behind step judges by.</summary>
/// <param name="LookAround">Largest distance from a target object to the target's visible objects that count as its surroundings.</param>
/// <param name="MinOccupiedDirections">Count of the 8 directions (1-8) that must be occupied for the direction rule to apply.</param>
/// <param name="DirectionClearedPercent">Removed share of a direction's ground area (10-100) that makes the direction cleared.</param>
/// <param name="ClearedDirectionsPercent">Share of the occupied directions (10-100) that must be cleared for removal.</param>
public sealed record LeftBehindSurroundings(float LookAround, int MinOccupiedDirections, int DirectionClearedPercent, int ClearedDirectionsPercent);
