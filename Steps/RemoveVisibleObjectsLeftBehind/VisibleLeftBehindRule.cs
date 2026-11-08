using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>
/// The verdict on the target's visible objects whose surroundings, the target's other visible objects around them, were removed.
/// An object whose mesh holds only effect shapes is drawn too, so it is judged, but it is no surroundings of other objects.
/// </summary>
internal sealed class VisibleLeftBehindRule(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IBaseFacts bases,
    VisibleLeftBehindSettings settings,
    IKindsAlwaysKept kindsAlwaysKept,
    ISurroundingsJudging judging)
{
    /// <param name="decisions">The decisions of the earlier steps: the objects they decided are not judged again.</param>
    /// <returns>Target indexes of the shown targets <paramref name="decisions"/> left undecided, ascending.</returns>
    public int[] Candidates(IRemovalDecisions decisions) =>
        judging.Candidates(targets, decisions, target => shapes.VisibilityOf(target).IsShown);

    public VisibleLeftBehindCheck Judge(MeasuredSurroundings measured)
    {
        var target = targets[measured.TargetIndex];
        var kind = VisibleObjectKinds.Of(target, bases);
        var isProtectedKind = kindsAlwaysKept.Keeps(target);
        return new VisibleLeftBehindCheck(measured.TargetIndex, kind, measured.Surroundings, Decide(measured.Surroundings, isProtectedKind), KeepReason: null);
    }

    /// <summary>The minimum number of objects around comes before the directions, so it is the reason named when both keep the object.</summary>
    private LeftBehindOutcome Decide(SectorAreas surroundings, bool isProtectedKind) =>
        surroundings.Objects < settings.MinObjectsAround
            ? LeftBehindOutcome.KeptTooFewObjectsAround
            : judging.Judge(surroundings, settings.Surroundings, isInsideAnotherModsObject: false, isProtectedKind);
}
