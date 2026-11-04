using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>Finds the target objects on terrain that another mod changed, and proposes their removal.</summary>
public static class ChangedTerrainObjects
{
    /// <param name="Checks">The changed-terrain checks over the targets of <paramref name="World"/>.</param>
    /// <param name="Decisions">The run's decisions so far, which the step's round is added to.</param>
    public sealed record Input(
        CollectedObjects World,
        IRunDecisions Decisions,
        IChangedTerrainChecks Checks,
        Execution Execution,
        IEnumerable<ObjectsJudgedWith<ChangedTerrainSettings>> Groups);

    /// <summary>
    /// Runs once for each distinct set of settings over the objects judged with it, and proposes the removals of all of them in target order.
    /// </summary>
    /// <remarks>
    /// A pass whose step is off reads no terrain and is <see cref="StepOutcome.SwitchedOff"/>. A group switched on that counts no change
    /// or has no area runs as off, so it merges with the groups switched off.
    /// </remarks>
    public static Decided<ChangedTerrainResult> Find(Input input)
    {
        ChangedTerrainResult result = new(
            [
                .. ObjectsJudgedWith<ChangedTerrainSettings>.MergeEqualSettings(input.Groups.Select(group => group with { Settings = group.Settings.AsRun }))
                    .Select(group => FindPass(input, group)),
            ]);
        return new Decided<ChangedTerrainResult>(result, input.Decisions.Apply(RoundKind.ChangedTerrain, result.Proposals));
    }

    private static ChangedTerrainPass FindPass(Input input, ObjectsJudgedWith<ChangedTerrainSettings> group)
    {
        if (input.Checks.Find(group.Objects, group.Settings, input.World.Order, input.Execution) is not { } found) return ChangedTerrainPass.SwitchedOff(group.Objects);

        return new ChangedTerrainPass(
            group.Objects,
            StepOutcome.Performed,
            found.Hits,
            [.. found.Hits.Select(hit => new ProposedRemoval(hit.Target, new TerrainChanged(hit.ChangedBy, hit.Kind)))],
            found.Failures,
            found.CellsCompared,
            found.ObjectsWithoutTerrain);
    }
}
