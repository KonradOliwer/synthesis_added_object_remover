using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The largest gaps in a cell or LAND record that are no change.</summary>
/// <param name="LandHeight">At a LAND grid point, in game units.</param>
/// <param name="WaterHeight">Of a cell's water height, in game units.</param>
public readonly record struct AreaPartTolerances(float LandHeight, float WaterHeight);

/// <summary>
/// Which records of other plugins the target plugin changed in the target spaces are read with their versions and judged part by
/// part, and with which tolerances. The navmeshes it overrides, deletes or adds are always read.
/// </summary>
/// <param name="Cells">The cell records it overrides.</param>
/// <param name="Lands">The LAND records it overrides.</param>
public sealed record AreaRecordReads(AreaPartTolerances Tolerances, bool Cells, bool Lands);

/// <summary>One part of a cell record and what happens to it.</summary>
public sealed record PartChange(CellPartName Name, PartOutcome Outcome);

/// <summary>
/// A cell record of another plugin that the target plugin overrides in a target space, with each part it changed, judged over the
/// versions from the one just below the target plugin's up to the winner, the patch included.
/// </summary>
/// <param name="SpaceKey">The worldspace, or for an interior the cell itself.</param>
/// <param name="Grid">Null for an interior.</param>
/// <param name="Parts">
/// Every part that is not <see cref="PartOutcome.Unchanged"/>, in the order of the cell's part table. When any version deletes the
/// cell, nothing goes back, so only the parts left to later plugins are listed.
/// </param>
/// <param name="EarlierWater">The water fields of the version just below the target plugin's.</param>
/// <param name="CurrentWater">The water fields of the winning version.</param>
/// <param name="DefaultWaterLevel">
/// The winning default water height of the worldspace whose water the cell uses, which a cell storing no height of its own has; null
/// for an interior or where none is stored.
/// </param>
public sealed record CellRecordFact(
    RecordKey Key,
    RecordKey SpaceKey,
    (int X, int Y)? Grid,
    PluginName WinningMod,
    ImmutableArray<PartChange> Parts,
    CellWaterCopy EarlierWater,
    CellWaterCopy CurrentWater,
    float? DefaultWaterLevel);

/// <summary>
/// A LAND record of another plugin that the target plugin overrides in a worldspace whose land a target space uses, with each part
/// it changed, judged like a cell (<see cref="CellRecordFact"/>).
/// </summary>
/// <param name="Cell">The cell of the worldspace that holds the LAND record.</param>
/// <param name="Heights">
/// Kept when some version changed a height or normal, or the two versions hold different heights; null otherwise, when the ground is
/// the same in both and is read again where needed. Normals and colours are not kept; a writer reads them from the record's versions.
/// </param>
/// <param name="CellDeletedInAVersion">A version read of the cell holding the LAND record deletes the cell.</param>
public sealed record LandRecordFact(RecordKey Key, ExteriorCell Cell, PluginName WinningMod, LandPartChanges Changes, LandHeights? Heights, bool CellDeletedInAVersion);

/// <summary>What the target plugin last changed in a LAND record, part by part (<see cref="PartHistory"/>).</summary>
/// <param name="UndoPoints">The points whose earlier height and normal go back.</param>
/// <param name="UndoColours">The points whose earlier vertex colour goes back.</param>
/// <param name="UndoQuarters">The quarters whose earlier texture layers go back.</param>
/// <param name="UndoRest">Whether the earlier rest of the record goes back.</param>
/// <remarks>When any version deletes the LAND record, nothing goes back.</remarks>
public sealed record LandPartChanges(PointSet UndoPoints, PointSet UndoColours, QuarterSet UndoQuarters, bool UndoRest, LandLeftToLater LeftToLater)
{
    public bool IsEmpty =>
        UndoPoints.IsEmpty && UndoColours.IsEmpty && UndoQuarters.IsEmpty && !UndoRest
        && LeftToLater.Points.IsEmpty && LeftToLater.Colours.IsEmpty && LeftToLater.Quarters.IsEmpty && !LeftToLater.Rest;
}

/// <summary>The parts of a LAND record the target plugin changed and a later plugin or the patch changed again.</summary>
/// <param name="Plugins">The last plugin to change each of those parts, each plugin once, in load order.</param>
public sealed record LandLeftToLater(PointSet Points, PointSet Colours, QuarterSet Quarters, bool Rest, ImmutableArray<PluginName> Plugins);

/// <summary>The heights at a LAND record's grid points in game units, row by row from the bottom, x fastest; each is null when its version has no height map.</summary>
/// <param name="Earlier">Of the version just below the target plugin's.</param>
/// <param name="Current">Of the winning version.</param>
public sealed record LandHeights(float[]? Earlier, float[]? Current);
