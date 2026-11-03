using System.Collections.Immutable;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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
/// <param name="Times">Where the step spent its time; none for a step that did not run its parts.</param>
/// <param name="Leftovers">The cleaned mod's objects that still stood in a restored area, in target order.</param>
/// <param name="OtherModsObjects">
/// What became of other mods' objects left without support, also of those the leftovers removed held up; the result of that step
/// when the step removed none.
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
    ImmutableArray<PartFailure> Failures,
    RestoreWhereRemovedTimes Times,
    ImmutableArray<LeftoverJudged> Leftovers,
    OtherModsObjectsResult OtherModsObjects)
{
    /// <param name="otherModsObjects">What the step for other mods' objects left without support found.</param>
    public static RestoreWhereRemovedResult SwitchedOff(OtherModsObjectsResult otherModsObjects) =>
        new(StepOutcome.SwitchedOff, [], [], [], [], [], Passes: 0, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None, Leftovers: [], otherModsObjects);
}
