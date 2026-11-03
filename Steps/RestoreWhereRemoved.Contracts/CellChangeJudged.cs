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
