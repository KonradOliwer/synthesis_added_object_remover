using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

internal static class ContainingObjectFinder
{
    /// <summary>
    /// Finds the other-mod object that can cause removals which each invisible target object sits inside; the lowest-id one
    /// when several contain it. Placed NPCs never host because their bases have no mesh, so whether they can cause removals
    /// does not change the result.
    /// </summary>
    /// <remarks>Only invisible targets have a containing object.</remarks>
    public static ContainingObjects Find(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IObjectsThatCanCauseRemovals otherModObjects,
        WorkOrder order,
        Execution execution)
    {
        return new([.. ParallelMap.Run(
            execution,
            order,
            targets.Count,
            () => new ObjectQueryScratch(),
            (i, scratch) => shapes.VisibilityOf(targets[i]).Kind != null ? FindContainingObject(targets[i], otherModObjects, scratch) : null,
            ParallelMap.AutomaticRangeSize)]);
    }

    private static OtherObject? FindContainingObject(TargetObject target, IObjectsThatCanCauseRemovals otherModObjects, ObjectQueryScratch scratch) =>
        otherModObjects.FirstCovering(target.SpaceKey, target.Transform.Position, scratch) is { } container ? otherModObjects.Get(container) : null;
}
