using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>
/// The verdict on the target's visible objects whose surroundings, the target's other visible objects around them, were removed.
/// The rule only judges; the removal decisions keep referenced objects and remove the linked groups of the removals.
/// </summary>
internal sealed class VisibleLeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IBaseFacts bases,
    VisibleLeftBehindSettings config)
{
    /// <param name="decisions">The decisions of the earlier steps: the objects they decided are not judged again.</param>
    /// <returns>Target indexes of the visible targets <paramref name="decisions"/> left undecided, ascending.</returns>
    public int[] Candidates(IRemovalDecisions decisions) =>
        [.. Enumerable.Range(0, targets.Count).Where(index => shapes.VisibilityOf(targets[index]).IsVisible && !decisions.IsDecided(targets[index].Id))];

    public VisibleLeftBehindCheck Judge(MeasuredSurroundings measured)
    {
        var kind = VisibleObjectKinds.Of(targets[measured.TargetIndex], bases);
        var decision = LeftBehindVerdicts.KeptWhenProtected(
            LeftBehindVerdicts.BySurroundings(measured.Surroundings, config.Surroundings), kind is { } listed && config.NeverRemove.Contains(listed));
        return new VisibleLeftBehindCheck(measured.TargetIndex, kind, measured.Surroundings, decision, KeepReason: null);
    }
}
