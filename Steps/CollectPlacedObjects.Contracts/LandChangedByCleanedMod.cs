using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// A LAND record of another plugin, in a worldspace whose land a target space uses, whose parts the cleaned mod changed, with what
/// goes back to its version just below the cleaned mod's and what a later plugin changed again. A LAND record that any version
/// deletes has nothing that goes back.
/// </summary>
/// <param name="Cell">The cell of the worldspace that holds the LAND record.</param>
/// <param name="UndoPoints">The points whose earlier height and normal go back.</param>
/// <param name="UndoColours">The points whose earlier vertex colour goes back.</param>
/// <param name="UndoQuarters">The quarters whose earlier texture layers go back.</param>
/// <param name="UndoRest">Whether the earlier rest of the record goes back.</param>
/// <param name="Heights">
/// The heights of the version just below the cleaned mod's and of the winning version; null when no version changed a height or
/// normal and both hold the same heights, which are then read where needed.
/// </param>
/// <param name="CellDeletedInAVersion">A version read of the cell holding the LAND record deletes the cell.</param>
/// <param name="EarlierPlugin">The plugin of the version just below the cleaned mod's, whose values go back; null when not read.</param>
public sealed record LandChangedByCleanedMod(
    ExteriorCell Cell,
    RecordKey LandKey,
    PointSet UndoPoints,
    PointSet UndoColours,
    QuarterSet UndoQuarters,
    bool UndoRest,
    LandLeftToLater LeftToLater,
    PluginName WinningMod,
    LandHeights? Heights,
    bool CellDeletedInAVersion = false,
    PluginName? EarlierPlugin = null)
{
    /// <summary>Some height, colour, texture or the rest of the record goes back.</summary>
    public bool HasPartsToUndo => !UndoPoints.IsEmpty || !UndoColours.IsEmpty || !UndoQuarters.IsEmpty || UndoRest;
}

/// <summary>The parts of a LAND record the cleaned mod changed and a later plugin or the patch changed again.</summary>
/// <param name="Plugins">The last plugin to change each of those parts, each plugin once, in load order.</param>
public sealed record LandLeftToLater(
    PointSet Points,
    PointSet Colours,
    QuarterSet Quarters,
    bool Rest,
    ImmutableArray<PluginName> Plugins)
{
    public bool HasParts => !Points.IsEmpty || !Colours.IsEmpty || !Quarters.IsEmpty || Rest;
}
