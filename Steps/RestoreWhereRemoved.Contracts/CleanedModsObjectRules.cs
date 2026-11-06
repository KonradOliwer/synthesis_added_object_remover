using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>What the removal steps judge one of the cleaned mod's own objects with, which the restore judges a planned copy with.</summary>
/// <param name="TooClose">The too-close settings of the cleaned mod's own objects.</param>
/// <param name="ChangedTerrainSettings">The changed-terrain settings of the cleaned mod's own objects.</param>
public sealed record CleanedModsObjectRules(TooCloseSettings TooClose, ChangedTerrainSettings ChangedTerrainSettings);
