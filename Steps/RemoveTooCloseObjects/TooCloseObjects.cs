using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Finds the target objects too close to another mod's object, by a multiple of their size and/or by a distance in game units, and
/// proposes their removal.
/// </summary>
public static class TooCloseObjects
{
    /// <param name="World">The world the step measures in, among whose other-mod objects it counts the ones it leaves out.</param>
    /// <param name="Searches">The too-close searches over <paramref name="World"/>.</param>
    /// <param name="Collected">The run's objects, among whose other-mod objects the step counts the ones it leaves out.</param>
    /// <param name="Order">The order the passes work through the target objects in.</param>
    /// <param name="Decisions">The run's decisions so far, which the step's round is added to.</param>
    public sealed record Input(
        TooCloseWorld World,
        ITooCloseSearches Searches,
        CollectedObjects Collected,
        WorkOrder Order,
        Execution Execution,
        IEnumerable<ObjectsJudgedWith<TooCloseSettings>> Groups,
        IRunDecisions Decisions);

    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    public static Decided<TooCloseResult> Find(Input input)
    {
        ImmutableArray<TooClosePass> passes = [.. ObjectsJudgedWith<TooCloseSettings>.MergeEqualSettings(input.Groups).Select(group => FindPass(input, group))];
        TooCloseResult result = new(passes, LeftOutOf(input, passes));
        return new Decided<TooCloseResult>(result, input.Decisions.Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <summary>Nothing is left out when no pass performed.</summary>
    private static OtherObjectsLeftOut LeftOutOf(Input input, ImmutableArray<TooClosePass> passes) =>
        passes.Any(pass => pass.Outcome == StepOutcome.Performed)
            ? OtherObjectsLeftOutCounter.Take(input.Collected, input.World.Shapes, input.World.VisibleTargets)
            : new OtherObjectsLeftOut([], PlacedNpcs: 0);

    /// <remarks>When no removal zone is on, the pass looks at nothing, not even for stuck NPCs, and its outcome is <see cref="StepOutcome.SwitchedOff"/>.</remarks>
    private static TooClosePass FindPass(Input input, ObjectsJudgedWith<TooCloseSettings> group)
    {
        var settings = group.Settings;
        if (input.Searches.Find(group.Objects, settings, input.Order, input.Execution) is not { } found) return SwitchedOff(group);

        return new TooClosePass(
            group.Objects,
            StepOutcome.Performed,
            found.Hits,
            [.. found.Hits.Select(hit => new ProposedRemoval(new TargetId(hit.TargetIndex), new TooClose(hit.TooCloseTo.Id, hit.Cause)))],
            found.Work,
            found.Npc,
            found.LargeOtherObjects,
            found.Failures,
            found.ZonesTested,
            settings.Zone,
            settings.Npcs);
    }

    private static TooClosePass SwitchedOff(ObjectsJudgedWith<TooCloseSettings> group) =>
        new(group.Objects, StepOutcome.SwitchedOff, [], [], TooCloseWork.Zero, Npc: null, LargeOtherObjects: 0, [], ZonesTested: [], group.Settings.Zone, group.Settings.Npcs);
}
