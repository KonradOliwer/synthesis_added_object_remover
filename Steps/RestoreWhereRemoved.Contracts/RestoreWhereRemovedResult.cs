using System.Collections.Immutable;

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
/// stopped, in record key order, then one per object whose changed-terrain check stopped, in key order.
/// </param>
/// <param name="Times">Where the step spent its time; none for a step that did not run its parts.</param>
/// <param name="TerrainFindingsLeft">
/// How many objects that stay, or copies, the changed-terrain rule still finds on terrain changed in the world as the restore leaves
/// it, where the restore writes nothing of what the check counts.
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
    int TerrainFindingsLeft = 0)
{
    /// <summary>What the removal steps' follow-up does once the placed records go back; none for a step that judged nothing.</summary>
    public RestoreFollowUp FollowUp { get; init; } = RestoreFollowUp.None;

    public static RestoreWhereRemovedResult SwitchedOff() =>
        new(StepOutcome.SwitchedOff, [], [], [], [], [], Passes: 0, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
}
