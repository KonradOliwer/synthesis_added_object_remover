using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;

/// <summary>
/// The other-mod object that can cause removals which each invisible target object sits inside; the lowest-id one when several contain it.
/// Placed NPCs never host because their bases have no mesh, so whether they can cause removals does
/// not change the hosts.
/// </summary>
internal sealed class Hosts
{
    private readonly OtherObject?[] _hosts;

    private Hosts(OtherObject?[] hosts)
    {
        _hosts = hosts;
    }

    /// <summary>Null for a target that is visible or sits inside no object.</summary>
    public OtherObject? HostOf(int targetIndex) => _hosts[targetIndex];

    /// <remarks>Only invisible targets have a host.</remarks>
    public static Hosts Find(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IObjectsThatCanCauseRemovals otherModObjects,
        WorkOrder order,
        Execution execution)
    {
        return new(ParallelMap.Run(
            execution,
            order,
            targets.Count,
            () => new ObjectQueryScratch(),
            (i, scratch) => shapes.VisibilityOf(targets[i]).Kind != null ? FindHost(targets[i], otherModObjects, scratch) : null,
            ParallelMap.AutomaticRangeSize));
    }

    private static OtherObject? FindHost(TargetObject target, IObjectsThatCanCauseRemovals otherModObjects, ObjectQueryScratch scratch) =>
        otherModObjects.FirstCovering(target.SpaceKey, target.Transform.Position, scratch) is { } host ? otherModObjects.Get(host) : null;
}
