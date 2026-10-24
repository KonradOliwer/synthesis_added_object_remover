using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// The step's check that depends on where an object stands: an invisible target object inside another mod's object is removed,
/// unless its kind is one the step never removes. Whether its surroundings were removed depends on the other removals, not on the
/// object's placement, so it is not part of this check.
/// </summary>
/// <param name="settings">Null when the step is switched off: it finds nothing.</param>
internal sealed class LeftBehindPlacementRule(
    ImmutableArray<TargetObject> targets,
    IObjectsThatCanCauseRemovals otherModObjects,
    IBaseObjectShapes shapes,
    LeftBehindSettings? settings) : IPlacementRule
{
    public PlacementCheck Check(TargetId target, EulerPlacement placement)
    {
        if (settings is null || shapes.VisibilityOf(targets[target.Index]).Kind is not { } kind || settings.NeverRemove.Contains(kind)) return default;
        try
        {
            var container = ContainingObjectFinder.FindContainingObject(targets[target.Index].PlacedAt(placement), otherModObjects, new ObjectQueryScratch());
            return new PlacementCheck(container is { } inside ? new RemovalReason.InsideAnotherModsObject(inside.Id) : null, Failure: null);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            return new PlacementCheck(Clash: null, Failures.DescribeFailure(ex));
        }
    }
}
