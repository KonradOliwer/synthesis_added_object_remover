using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <param name="BySize">Whether the removal zone enlarged by <paramref name="Multiplier"/> is on; the step is switched off when no zone is on.</param>
/// <param name="Multiplier">How much a target object's removal zone is enlarged, as a multiple of its size.</param>
/// <param name="ByDistance">Whether the removal zone enlarged by <paramref name="DistanceInUnits"/> is on.</param>
/// <param name="DistanceInUnits">How far from a target object another mod's object counts as too close, in game units.</param>
public sealed record TooCloseSettings(
    bool BySize, float Multiplier, bool ByDistance, float DistanceInUnits, ZoneShape Zone, NpcHandling Npcs);
