using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The largest gaps in a cell or LAND record that are no change.</summary>
/// <param name="LandHeight">At a LAND grid point, in game units.</param>
/// <param name="WaterHeight">Of a cell's water height, in game units.</param>
public readonly record struct AreaPartTolerances(float LandHeight, float WaterHeight);

/// <summary>
/// Which records of other plugins the read's plugin changed in the plugin spaces are read with their versions and compared part
/// by part, and with which tolerances. The navmeshes it overrides, deletes or adds are always read.
/// </summary>
/// <param name="Cells">The cell records it overrides.</param>
/// <param name="Lands">The LAND records it overrides.</param>
public sealed record AreaRecordReads(AreaPartTolerances Tolerances, bool Cells, bool Lands);

/// <summary>One part of a cell record and how it differs across the record's versions.</summary>
public sealed record CellPartDifferences(CellPartName Name, PartDifferences Differences);

/// <summary>
/// A cell record of another plugin that the read's plugin overrides in a plugin space and changed, with how each of its parts
/// differs across the versions from the one just below the read's plugin's up to the winner, the patch included.
/// </summary>
/// <param name="SpaceKey">The worldspace, or for an interior the cell itself.</param>
/// <param name="Grid">Null for an interior.</param>
/// <param name="Parts">In the order of the cell's part table.</param>
/// <param name="DeletedInAVersion">A version read deletes the cell.</param>
/// <param name="EarlierWater">The water fields of the version just below the read's plugin's.</param>
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
    ImmutableArray<CellPartDifferences> Parts,
    bool DeletedInAVersion,
    CellWaterCopy EarlierWater,
    CellWaterCopy CurrentWater,
    float? DefaultWaterLevel);

/// <summary>
/// A LAND record of another plugin that the read's plugin overrides in a worldspace whose land a plugin space uses and changed,
/// with how its parts differ across its versions, read like a cell (<see cref="CellRecordFact"/>). No LAND part holds bytes Mutagen
/// does not read, so every earlier value can be written back.
/// </summary>
/// <param name="Cell">The cell of the worldspace that holds the LAND record.</param>
/// <param name="DeletedInAVersion">A version read deletes the LAND record.</param>
/// <param name="Heights">
/// Kept when some version changed a height or normal, or the two versions hold different heights; null otherwise, when the ground is
/// the same in both and is read again where needed. Normals and colours are not kept; a writer reads them from the record's versions.
/// </param>
/// <param name="CellDeletedInAVersion">A version read of the cell holding the LAND record deletes the cell.</param>
public sealed record LandRecordFact(
    RecordKey Key,
    ExteriorCell Cell,
    PluginName WinningMod,
    ImmutableArray<VersionDifferences<LandParts>> Versions,
    bool DeletedInAVersion,
    LandHeights? Heights,
    bool CellDeletedInAVersion);

/// <summary>Parts of a LAND record.</summary>
/// <param name="Points">The points by their height and normal.</param>
/// <param name="Colours">The points by their vertex colour.</param>
/// <param name="Quarters">The quarters by their texture layers.</param>
/// <param name="Rest">The rest of the record.</param>
public sealed record LandParts(PointSet Points, PointSet Colours, QuarterSet Quarters, bool Rest)
{
    public bool IsEmpty => Points.IsEmpty && Colours.IsEmpty && Quarters.IsEmpty && !Rest;
}

/// <summary>The heights at a LAND record's grid points in game units, row by row from the bottom, x fastest; each is null when its version has no height map.</summary>
/// <param name="Earlier">Of the version just below the read's plugin's.</param>
/// <param name="Current">Of the winning version.</param>
public sealed record LandHeights(float[]? Earlier, float[]? Current);
