using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// A cell record of another plugin in a target space whose parts the cleaned mod changed, with what goes back to its version just
/// below the cleaned mod's and what a later plugin changed again.
/// </summary>
/// <param name="SpaceKey">The worldspace, or for an interior the cell itself.</param>
/// <param name="Grid">Null for an interior.</param>
/// <param name="Parts">
/// Every part the cleaned mod changed, in the order of the cell's parts. A cell that any version deletes has only its parts left to
/// later plugins.
/// </param>
/// <param name="LinkedFromCleanedMod">A record the cleaned mod creates, not removed in the game, links to it.</param>
/// <param name="EarlierWater">The water fields of the version just below the cleaned mod's.</param>
/// <param name="CurrentWater">The water fields of the winning version.</param>
/// <param name="DefaultWaterLevel">
/// The winning default water height of the worldspace whose water the cell uses, which a cell storing no height of its own has; null
/// for an interior or where none is stored.
/// </param>
public sealed record CellChangedByCleanedMod(
    RecordKey Cell,
    RecordKey SpaceKey,
    (int X, int Y)? Grid,
    ImmutableArray<PartChange> Parts,
    bool LinkedFromCleanedMod,
    PluginName WinningMod,
    CellWaterCopy EarlierWater,
    CellWaterCopy CurrentWater,
    float? DefaultWaterLevel);

/// <summary>One part of a cell record and what happens to it.</summary>
public sealed record PartChange(CellPartName Name, PartOutcome Outcome);
