using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

internal static class TargetWorkOrder
{
    /// <summary>Orders the targets by space, then by exterior cell, so neighbouring targets run close together in time and their meshes are still cached.</summary>
    public static WorkOrder Of(IReadOnlyList<TargetObject> targets) =>
        new(LocalityOrder.Of(targets, target => target.SpaceKey, target => target.Transform.Position));
}
