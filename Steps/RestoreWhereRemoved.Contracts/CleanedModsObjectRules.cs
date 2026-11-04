using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What the removal steps judge one of the cleaned mod's own objects with, which the restore judges a planned copy with.</summary>
/// <param name="TooClose">The too-close settings of the cleaned mod's own objects.</param>
/// <param name="ChangedTerrain">The changed-terrain step's check of one object with the settings of the cleaned mod's own objects; it finds nothing when that step is off.</param>
public sealed record CleanedModsObjectRules(TooCloseSettings TooClose, IPlacementRule ChangedTerrain);
