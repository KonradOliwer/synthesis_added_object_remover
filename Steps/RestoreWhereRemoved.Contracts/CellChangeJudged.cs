using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>A cell record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Undone">
/// The parts the cleaned mod is the last to change, in the order of the cell's parts, but the water level when <paramref name="WaterKeptBy"/> keeps it.
/// </param>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Area">The area around the cell's centre, or the whole interior; null when <paramref name="Never"/> is set.</param>
/// <param name="WaterKeptBy">
/// What keeps the cleaned mod's water level, by <paramref name="WaterKeptFor"/>: the first navmesh that stays changed found lying
/// partly between the water level before the cleaned mod and the cleaned mod's, a placed record that stays where the cleaned mod put it,
/// or leaves a copy there, found lying so, an object that stays whose changed-terrain check would count
/// the level going back, or the original of such a copy; null when
/// the water level goes back or the cleaned mod is not the last to change it.
/// </param>
/// <param name="WaterKeptFor">Why <paramref name="WaterKeptBy"/> keeps the water level; it means nothing while that is null.</param>
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
    AreaVerdict? KeptForArea,
    WaterKeptReason WaterKeptFor = WaterKeptReason.NavmeshAtWaterLine);

/// <summary>Why a cell keeps the cleaned mod's water level although its other parts may go back.</summary>
public enum WaterKeptReason
{
    /// <summary>A navmesh that stays changed lies partly between the level before the cleaned mod and the cleaned mod's.</summary>
    NavmeshAtWaterLine,

    /// <summary>
    /// An object that stays where the cleaned mod put it lies partly between the level before the cleaned mod and the cleaned mod's: a
    /// placed record whose areas let it go back that stays, or the copy left for one that goes back; the keeper named is that record.
    /// </summary>
    ObjectAtWaterLine,

    /// <summary>
    /// The changed-terrain rule, checking an object that stays or a copy the patch adds as the next patcher reads the planned world,
    /// would count the level going back, or that check stopped where it reads the cell; the keeper named is that object, or the copy's
    /// original.
    /// </summary>
    ForChangedTerrainCheck,

    /// <summary>
    /// Only a later round of the restore cleared the cell, and its water level going back would undo what an earlier round put back or
    /// removed; the keeper named is the record whose outcome it would undo.
    /// </summary>
    ForEarlierRound,

    /// <summary>
    /// Only a later round of the restore cleared the cell, and that round's parts going back together would undo what an earlier round
    /// put back or removed, with no single cause found; the keeper named is the record whose outcome they would undo.
    /// </summary>
    ForEarlierRoundTogether,
}
