using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>How much of the cleaned mod's objects still stand in an area.</summary>
public enum AreaVerdict
{
    /// <summary>The area holds none of the cleaned mod's visible objects, so its changes stay.</summary>
    NoObjectsAround,

    /// <summary>At most the allowed share still stands.</summary>
    Cleared,

    /// <summary>More than the allowed share still stands.</summary>
    StillStanding,
}

/// <summary>The cleaned mod's visible objects in an area and how many of them still stand.</summary>
/// <param name="Objects">The objects counted.</param>
/// <param name="Standing">Those not removed.</param>
/// <param name="Size">The box volume of the objects counted, in cubic game units.</param>
/// <param name="StandingSize">The box volume of those not removed.</param>
/// <param name="Measure">What the share is weighed by: the setting's measure, or the count when the setting weighs by size and no object counted has a volume.</param>
/// <param name="StandingPercent">The percent of the weight that still stands; 0 when the area holds no object.</param>
public sealed record AreaShare(int Objects, int Standing, double Size, double StandingSize, AreaVerdict Verdict, ClearedAreaMeasure Measure, double StandingPercent);

/// <summary>Why a change never goes back, whatever its area.</summary>
public enum NeverRestoredReason
{
    /// <summary>A plugin after the patch wins the record, so the patch's override would not show.</summary>
    WinnerLoadsAfterPatch,

    /// <summary>A record the cleaned mod creates links to it, and the cleaned mod's quests and scripts still run.</summary>
    LinkedFromCleanedMod,

    /// <summary>A plugin deletes a cell it would be written into, and nothing is written into a deleted cell.</summary>
    InDeletedCell,
}

/// <summary>What keeps a placed record whose area is cleared as the cleaned mod made it.</summary>
public enum PlacedGuard
{
    /// <summary>Where it goes back to, it comes too close to an object that stays, by the collision settings of the objects the cleaned mod changed.</summary>
    TooCloseToWhatStays,

    /// <summary>
    /// Where it goes back to, ground that stays changed lies under it or the area around it: ground the terrain plan keeps as the
    /// cleaned mod made it, or ground a later plugin changed.
    /// </summary>
    OnGroundThatStaysChanged,

    /// <summary>Where it goes back to, it lies partly between a cell's water level before the cleaned mod and the one that stays.</summary>
    AtWaterLineThatStaysChanged,

    /// <summary>Where it goes back to, it holds an invisible object of the cleaned mod that stays.</summary>
    HoldsInvisibleObjectThatStays,

    /// <summary>Where it goes back to, it stands in a navmesh that stays as the cleaned mod made it, so actors would walk into it.</summary>
    StandsInChangedNavmesh,

    /// <summary>An object that stays rests on it where it stands now, and would float once it goes back.</summary>
    CarriesObjectThatStays,

    /// <summary>It is a door whose navmesh link goes back with it, and a later plugin changed that link again.</summary>
    DoorLinkChangedLater,

    /// <summary>
    /// It is a door whose navmesh link goes back with it, and its link before the cleaned mod would not match the navmeshes as they end:
    /// the navmesh it names goes back in no group and does not answer it, or a navmesh that stays names the door elsewhere.
    /// </summary>
    DoorLinkDoesNotMatch,
}

/// <summary>The first guard that keeps a placed record as the cleaned mod made it.</summary>
/// <param name="By">
/// The object, navmesh, LAND record or cell the guard found; null when the check stopped, or for ground of a LAND record the cleaned
/// mod does not override.
/// </param>
/// <param name="Failure">Why the check stopped unexpectedly, which keeps the record too; null when it did not.</param>
public sealed record PlacedRefusal(PlacedGuard Guard, RecordKey? By, string? Failure);

/// <summary>What a placed record's restore puts back, by the first of these its undone parts hold.</summary>
public enum PlacedRestoreKind
{
    /// <summary>Its base object with every field no other part holds.</summary>
    OwnModel,

    /// <summary>Its presence, which makes a record removed now present again.</summary>
    ShownAgain,

    /// <summary>Its presence, which makes a record present now removed again.</summary>
    HiddenAgain,

    /// <summary>Its position.</summary>
    MovedBack,

    /// <summary>Its rotation or scale.</summary>
    TurnedOrRescaledBack,
}

/// <summary>A placed record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="OldPlace">The area around the earlier version's place; null when <paramref name="Never"/> is set.</param>
/// <param name="WhereItIsNow">For a target the removal steps left undecided, the area where it stands now; null otherwise.</param>
/// <param name="Refused">The first guard that keeps it, once every area judged is cleared; null when none does.</param>
/// <param name="IsRestored">Its undone parts go back: no never-rule holds, every area judged is cleared and no guard keeps it.</param>
/// <param name="Kind">What its restore puts back.</param>
/// <param name="KeptForArea">
/// When no never-rule holds and an area judged is not cleared, the verdict of the area that keeps it: one where the cleaned mod's
/// objects still stand before one with none of them around; null otherwise.
/// </param>
/// <remarks>When it stays, exactly one of <paramref name="Never"/>, <paramref name="Refused"/> and <paramref name="KeptForArea"/> says why.</remarks>
public sealed record PlacedChangeJudged(
    RecordChangedByCleanedMod Change,
    NeverRestoredReason? Never,
    AreaShare? OldPlace,
    AreaShare? WhereItIsNow,
    PlacedRefusal? Refused,
    bool IsRestored,
    PlacedRestoreKind Kind,
    AreaVerdict? KeptForArea);

/// <summary>A cell record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Undone">
/// The parts the cleaned mod is the last to change, in the order of the cell's parts, but the water level when <paramref name="WaterKeptBy"/> keeps it.
/// </param>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Area">The area around the cell's centre, or the whole interior; null when <paramref name="Never"/> is set.</param>
/// <param name="WaterKeptBy">
/// The first object or navmesh found lying partly between the water level before the cleaned mod and the cleaned mod's, which keeps
/// the cleaned mod's water level; null when the water level goes back or the cleaned mod is not the last to change it.
/// </param>
/// <param name="IsRestored">Its undone parts go back: no never-rule holds, its area is cleared and some part is undone.</param>
/// <param name="KeptForArea">When no never-rule holds and its area is not cleared, the area's verdict; null otherwise.</param>
/// <remarks>
/// When it stays, exactly one of <paramref name="Never"/>, <paramref name="KeptForArea"/> and <paramref name="WaterKeptBy"/> says
/// why: a cell whose area is cleared stays only when its water level was all that would go back.
/// </remarks>
public sealed record CellChangeJudged(
    CellChangedByCleanedMod Change,
    ImmutableArray<PartChange> Undone,
    NeverRestoredReason? Never,
    AreaShare? Area,
    RecordKey? WaterKeptBy,
    bool IsRestored,
    AreaVerdict? KeptForArea);

/// <summary>One quarter of a cell's terrain, judged at its centre in every space whose ground the cell is.</summary>
/// <param name="Area">
/// The areas of every such space that holds a target added up, under the quarter's verdict: cleared when no space says the cleaned
/// mod's objects still stand and at least one says cleared.
/// </param>
public sealed record LandQuarterJudged(AreaShare Area)
{
    public AreaVerdict Verdict => Area.Verdict;
}

/// <summary>Heights, vertex colours and texture layers of a terrain quarter.</summary>
/// <param name="Heights">Grid points by their height.</param>
/// <param name="Colours">Grid points by their vertex colour.</param>
/// <param name="Textures">The quarter's texture layers.</param>
public readonly record struct TerrainPieces(int Heights, int Colours, bool Textures)
{
    public bool Any => Heights > 0 || Colours > 0 || Textures;
}

/// <param name="Points">How many of the quarter's points keep their height for <paramref name="Reason"/>.</param>
public sealed record QuarterPointsKept(TerrainKeptReason Reason, int Points);

/// <summary>
/// One quarter of a cell's terrain that holds something the cleaned mod last changed that could go back, and what became of it. A
/// point on the line between quarters counts in each of them; the rest of the LAND record belongs to no quarter.
/// </summary>
/// <param name="Quarter">By <see cref="TerrainQuadrants.IndexOf"/>.</param>
/// <param name="Area">The quarter's area; null when a never-rule keeps the land.</param>
/// <param name="Changed">What the cleaned mod last changed in it that could go back.</param>
/// <param name="Restored">What of that goes back.</param>
/// <param name="Later">What a later plugin changed again in it.</param>
/// <param name="Kept">In a cleared quarter, the points whose height stays per reason, in reason order; empty otherwise.</param>
/// <param name="KeptForArea">When nothing of it goes back and no never-rule holds, the verdict of its area that is not cleared; null otherwise.</param>
/// <param name="ColoursSharedWithGroundThatStays">
/// Nothing of the cleared quarter goes back and it keeps no height: its only change, vertex colours, each lies on a line shared with
/// a quarter or a neighbouring cell whose colour stays there.
/// </param>
/// <remarks>
/// When nothing of it goes back, exactly one of the land's never-rule, <paramref name="KeptForArea"/>, <paramref name="Kept"/> and
/// <paramref name="ColoursSharedWithGroundThatStays"/> says why.
/// </remarks>
public sealed record LandQuarterOutcome(
    int Quarter,
    AreaShare? Area,
    TerrainPieces Changed,
    TerrainPieces Restored,
    TerrainPieces Later,
    ImmutableArray<QuarterPointsKept> Kept,
    AreaVerdict? KeptForArea,
    bool ColoursSharedWithGroundThatStays);

/// <summary>Why a grid point whose height the cleaned mod last changed keeps that height although a quarter holding it is cleared.</summary>
public enum TerrainKeptReason
{
    /// <summary>An object of the cleaned mod's run that stays stands on it.</summary>
    UnderObjectThatStays,

    /// <summary>A placed record a guard keeps as the cleaned mod made it stands on it.</summary>
    UnderObjectKeptAsChanged,

    /// <summary>An object of another plugin that stays stands on it and sits closer to the ground as it is than to the ground that would go back.</summary>
    UnderObjectPlacedForTheGroundAsItIs,

    /// <summary>A navmesh as it will stand lies on the ground as it is there and would not lie on the ground that would go back.</summary>
    UnderNavmesh,

    /// <summary>The cell's heights with it restored would have a step too steep for the height map to store.</summary>
    TooSteepToStore,

    /// <summary>It lies on a cell edge, and the LAND record of a neighbouring cell keeps its height there.</summary>
    OnEdgeOfLandThatStays,

    /// <summary>It lies on a cell edge whose LAND records held different heights there before the cleaned mod.</summary>
    EarlierEdgeHeightsDiffer,

    /// <summary>It lies on a cell edge whose LAND records store heights on steps apart by a fraction of a step, so no height is stored alike in both.</summary>
    EdgeStoredOnDifferentSteps,

    /// <summary>It lies on the line between quarters of its cell, and a quarter holding it is not cleared.</summary>
    NextToQuarterThatStays,

    /// <summary>The LAND record's version just below the cleaned mod's or its winning version holds no height map, so no height can go back.</summary>
    WithoutHeightMap,
}

/// <param name="Points">Ascending; no point is listed under two reasons.</param>
public sealed record LandPointsKept(TerrainKeptReason Reason, PointSet Points);

/// <summary>A LAND record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Quarters">The four quarters by <see cref="TerrainQuadrants.IndexOf"/>; empty when <paramref name="Never"/> is set.</param>
/// <param name="Heights">The points whose height and normal go back, wholly or blended into the ground that stays.</param>
/// <param name="Colours">The points whose vertex colour goes back.</param>
/// <param name="Textures">The quarters whose texture layers go back.</param>
/// <param name="Rest">Whether the rest of the record goes back: the cleaned mod last changed it and every quarter is cleared.</param>
/// <param name="Kept">The points the cleaned mod last changed that a cleared quarter holds and whose height stays, by first reason in reason order.</param>
/// <param name="FinalHeights">Every point's height once written, in game units, row by row from the bottom; null when no height goes back.</param>
/// <param name="Beyond">The final heights just outside the cell, for the normals at its edges.</param>
/// <param name="IsRestored">Some height, colour, texture or the rest goes back.</param>
/// <param name="ChangedQuarters">The quarters that hold something the cleaned mod last changed that could go back, in quarter order.</param>
/// <param name="RestKeptForArea">
/// When the rest of the record the cleaned mod last changed stays and no never-rule holds, the verdict of a quarter that is not
/// cleared: one where the cleaned mod's objects still stand before one with none of them around; null otherwise.
/// </param>
public sealed record LandChangeJudged(
    LandChangedByCleanedMod Change,
    NeverRestoredReason? Never,
    ImmutableArray<LandQuarterJudged> Quarters,
    PointSet Heights,
    PointSet Colours,
    QuarterSet Textures,
    bool Rest,
    ImmutableArray<LandPointsKept> Kept,
    float[]? FinalHeights,
    CellEdgeHeights Beyond,
    bool IsRestored,
    ImmutableArray<LandQuarterOutcome> ChangedQuarters,
    AreaVerdict? RestKeptForArea);

/// <summary>What the patch writes for a navmesh of a group that goes back.</summary>
public enum NavmeshUndo
{
    /// <summary>The cleaned mod edited it: its geometry goes back, and its map entry when the cleaned mod last changed that too.</summary>
    Geometry,

    /// <summary>The cleaned mod edited only its map entry, which goes back.</summary>
    MapEntry,

    /// <summary>The cleaned mod deleted it: it comes back whole with its map entry.</summary>
    BroughtBack,

    /// <summary>The cleaned mod added it: it is lowered out of the walkable world and cut off.</summary>
    Sunk,
}

/// <summary>A navmesh the cleaned mod changed or added, and what its area says.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Area">The area around the centre of its bounds, or the whole interior; null when <paramref name="Never"/> is set or it has no vertices.</param>
/// <param name="Undo">What the patch writes when its group goes back; null when it is no candidate: a never-rule holds, its area is not cleared or nothing in it goes back.</param>
/// <param name="KeptForArea">When no never-rule holds and its area is not cleared, the area's verdict; null otherwise.</param>
/// <param name="NothingGoesBack">No never-rule holds and its area is cleared or it has no vertices to judge it at, but nothing in it goes back.</param>
/// <remarks>
/// When it is no candidate, exactly one of <paramref name="Never"/>, <paramref name="KeptForArea"/> and
/// <paramref name="NothingGoesBack"/> says why; a candidate goes back or stays with its group.
/// </remarks>
public sealed record NavmeshChangeJudged(
    NavmeshChangedByCleanedMod Change, NeverRestoredReason? Never, AreaShare? Area, NavmeshUndo? Undo, AreaVerdict? KeptForArea, bool NothingGoesBack);

/// <summary>What keeps a group of navmeshes as the cleaned mod made them (rules N0–N9).</summary>
public enum NavmeshGuard
{
    /// <summary>N0: a later plugin changed a member or a door linked to one again.</summary>
    ChangedLater,

    /// <summary>A member's geometry would go back but is stored in another record format.</summary>
    StoredInAnotherRecordFormat,

    /// <summary>N1: an edge link between a member as planned and a navmesh that stays would not be answered by a matching link back.</summary>
    EdgeLinkUnmatched,

    /// <summary>N7: a member would come back where it did not exist before the cleaned mod, or without its earlier map entry or replacement.</summary>
    DidNotExistBefore,

    /// <summary>N8: a plugin after the patch overrides the navmesh info map, or the preferred paths name a member whose Preferred triangles change.</summary>
    PreferredPaths,

    /// <summary>N2: a map entry would merge with a navmesh that is sunk or deleted.</summary>
    MapEntry,

    /// <summary>N3: a door named by a member would not stand, or a door and a navmesh would not name each other.</summary>
    Door,

    /// <summary>N9: the navmesh data the group brings back from before the cleaned mod is not consistent.</summary>
    EarlierDataInconsistent,

    /// <summary>N9: the navmesh data that stays as it stands is not consistent with the group.</summary>
    DataAsItStandsInconsistent,

    /// <summary>N4: the ground under a member as planned ends more than the height tolerance from its height before the cleaned mod.</summary>
    Ground,

    /// <summary>N5: an object that stays stands in a member as planned.</summary>
    Object,

    /// <summary>N6: the water level of a member's cell does not end as it was before the cleaned mod.</summary>
    Water,
}

/// <summary>The first guard that keeps a group of navmeshes as the cleaned mod made them.</summary>
/// <param name="By">The navmesh, door, object or cell the guard found.</param>
/// <param name="Fault">For <see cref="NavmeshGuard.EarlierDataInconsistent"/> and <see cref="NavmeshGuard.DataAsItStandsInconsistent"/>, the check's first fault; null otherwise.</param>
public sealed record NavmeshRefusal(NavmeshGuard Guard, RecordKey By, NavmeshFault? Fault);

/// <summary>Navmeshes that go back together or not at all, and what the patch writes for them.</summary>
/// <param name="Members">In key order.</param>
/// <param name="Orders">The navmesh, map entry and door link orders that put the group back.</param>
/// <param name="Refused">The first guard that keeps the group; null when none does.</param>
public sealed record NavmeshGroupJudged(ImmutableArray<RecordKey> Members, ImmutableArray<WriteOrder> Orders, NavmeshRefusal? Refused, bool IsRestored);

/// <param name="Objects">The placed records judged, in record key order.</param>
/// <param name="Cells">The cell records judged, in record key order.</param>
/// <param name="Lands">The LAND records judged, in (worldspace, x, y) order of their cells.</param>
/// <param name="Navmeshes">The navmeshes judged, in record key order.</param>
/// <param name="NavmeshGroups">The groups of candidate navmeshes, in the key order of their first member.</param>
/// <param name="Passes">How often the guards between the kinds were repeated until nothing more was refused, protected or kept.</param>
/// <param name="DoorsLeftRemoved">
/// The doors the navmeshes the cleaned mod changed name that end removed in the final world, by their final enable state and deletion
/// after this run's orders, in key order.
/// </param>
/// <param name="Failures">
/// The unexpected errors: one for the whole step, when it stopped and judged nothing; otherwise one per placed record whose check
/// stopped, in record key order.
/// </param>
public sealed record RestoreWhereRemovedResult(
    StepOutcome Outcome,
    ImmutableArray<PlacedChangeJudged> Objects,
    ImmutableArray<CellChangeJudged> Cells,
    ImmutableArray<LandChangeJudged> Lands,
    ImmutableArray<NavmeshChangeJudged> Navmeshes,
    ImmutableArray<NavmeshGroupJudged> NavmeshGroups,
    int Passes,
    ImmutableArray<RecordKey> DoorsLeftRemoved,
    ImmutableArray<PartFailure> Failures)
{
    public static readonly RestoreWhereRemovedResult SwitchedOff = new(StepOutcome.SwitchedOff, [], [], [], [], [], Passes: 0, DoorsLeftRemoved: [], Failures: []);
}
