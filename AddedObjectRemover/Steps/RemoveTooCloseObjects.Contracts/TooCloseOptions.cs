using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <param name="Multiplier">How much a target object's removal zone is enlarged.</param>
public sealed record TooCloseOptions(float Multiplier, ZoneShape Zone, NpcHandling Npcs);
