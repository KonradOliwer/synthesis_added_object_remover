using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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
