using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>
/// The verdict on the target's visible objects whose surroundings, the target's other visible objects around them, were removed.
/// An object whose mesh holds only effect shapes is drawn too, so it is judged, but it is no surroundings of other objects.
/// The rule only judges; the removal decisions keep referenced objects and remove the linked groups of the removals.
/// </summary>
internal sealed class VisibleLeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IBaseFacts bases,
    VisibleLeftBehindSettings config,
    IKindsAlwaysKept kindsAlwaysKept)
{
    /// <param name="decisions">The decisions of the earlier steps: the objects they decided are not judged again.</param>
    /// <returns>Target indexes of the shown targets <paramref name="decisions"/> left undecided, ascending.</returns>
    public int[] Candidates(IRemovalDecisions decisions) =>
        LeftBehindVerdicts.Candidates(targets, decisions, target => shapes.VisibilityOf(target).IsShown);

    public VisibleLeftBehindCheck Judge(MeasuredSurroundings measured)
    {
        var target = targets[measured.TargetIndex];
        var kind = VisibleObjectKinds.Of(target, bases);
        var isProtectedKind = kindsAlwaysKept.KindKeeping(target) != null;
        var decision = LeftBehindVerdicts.Judge(measured.Surroundings, config.Surroundings, isInsideAnotherModsObject: false, isProtectedKind);
        return new VisibleLeftBehindCheck(measured.TargetIndex, kind, measured.Surroundings, decision, KeepReason: null);
    }
}
