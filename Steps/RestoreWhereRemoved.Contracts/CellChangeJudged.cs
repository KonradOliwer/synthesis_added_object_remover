using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>A cell record of another plugin with parts that go back where the cleaned mod's objects are removed, and what became of it.</summary>
/// <param name="Undone">
/// The parts the cleaned mod is the last to change, in the order of the cell's parts, but the water level when <paramref name="WaterKeptBy"/> keeps it.
/// </param>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Area">The area around the cell's centre, or the whole interior; null when <paramref name="Never"/> is set.</param>
/// <param name="WaterKeptBy">
/// What keeps the cleaned mod's water level, by <paramref name="WaterKeptFor"/>: the first object or navmesh found lying partly
/// between the water level before the cleaned mod and the cleaned mod's, an object that stays, or the original of a copy; null when
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
    WaterKeptReason WaterKeptFor = WaterKeptReason.AtWaterLine);

/// <summary>Why a cell keeps the cleaned mod's water level although its other parts may go back.</summary>
public enum WaterKeptReason
{
    /// <summary>An object or navmesh that stays lies partly between the level before the cleaned mod and the cleaned mod's.</summary>
    AtWaterLine,

    /// <summary>
    /// The changed-terrain step checks the ground in the cell around an object that stays, with the settings of that object's own
    /// collision group, and counts water changes.
    /// </summary>
    AroundObjectThatStays,

    /// <summary>
    /// The changed-terrain step checks the ground in the cell around a copy the patch adds, with the settings of the cleaned mod's own
    /// objects, and counts water changes; the keeper named is the copy's original.
    /// </summary>
    AroundCopy,
}
