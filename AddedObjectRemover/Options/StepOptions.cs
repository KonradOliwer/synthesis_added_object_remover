namespace AddedObjectRemover;

/// <param name="Multiplier">How much a target object's removal zone is enlarged.</param>
internal sealed record ClashOptions(float Multiplier, ZoneShape Zone, NpcHandling Npcs);

/// <param name="TouchGap">Largest gap, in game units, between two surfaces that still counts as touching.</param>
/// <param name="SupportLostFraction">Fraction (0-1) of an object's support that must come from removed objects for ObjectsSupportedByIt to remove it.</param>
internal sealed record FollowUpOptions(FollowUpRemovalMode Mode, float TouchGap, float SupportLostFraction);

/// <param name="LookAround">Largest distance from an invisible target object to the target's visible objects that count as its surroundings.</param>
/// <param name="DirectionClearedPercent">Removed share of a direction's ground area (10-100) that makes the direction cleared.</param>
/// <param name="ClearedDirectionsPercent">Share of the occupied directions (10-100) that must be cleared for removal.</param>
/// <param name="OccupiedDirectionsPercent">Share of all directions (10-100) that must be occupied for the direction rule to apply.</param>
/// <param name="NeverRemove">Invisible object kinds that are never removed as leftovers.</param>
/// <param name="Preset">The protected-types choice the kinds were resolved from, for the settings echo.</param>
internal sealed record LeftoverOptions(
    float LookAround,
    int DirectionClearedPercent,
    int ClearedDirectionsPercent,
    int OccupiedDirectionsPercent,
    IReadOnlySet<InvisibleObjectKind> NeverRemove,
    ProtectedInvisibleObjectsPreset Preset);

/// <param name="MaxDistance">Largest distance, in game units, a kept marker is moved.</param>
internal sealed record RelocationOptions(float MaxDistance = KeptObjectRelocator.MaxMoveDistance);
