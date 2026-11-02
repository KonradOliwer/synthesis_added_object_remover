using System.Collections.Immutable;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// Has the patch writing write a trial's orders into a trial plugin, never into the patch, and checks the navmeshes they change over
/// the world those orders alone leave on top of the winning records read. Only the trial's own removals count as doors that do not stand.
/// </summary>
internal sealed class NavmeshCheckIfWritten(IPluginRecords plugin, IPatchWriting writing) : INavmeshCheckIfWritten
{
    private static readonly IReadOnlySet<RecordKey> NoDoorsLeftRemoved = ImmutableHashSet<RecordKey>.Empty;

    public NavmeshFault? FindFaultIfWritten(NavmeshWriteTrial trial)
    {
        var orders = OrdersOf(trial);
        return writing.ReadNavmeshesIfWritten(plugin.ReadWritableRecords(), orders, world => NavmeshWriteConsistency.FirstFault(orders, NoDoorsLeftRemoved, () => world));
    }

    /// <summary>
    /// The removals, then the restored parts, then the group's orders. Whether a door stands depends on its Initially Disabled flag
    /// alone, so a removed door is tried by that order only.
    /// </summary>
    private static List<WriteOrder> OrdersOf(NavmeshWriteTrial trial) =>
    [
        .. trial.RemovedDoors.Select(RemovalWrite.DisableOrderFor),
        .. trial.RestoredParts.Select(PlacedUndoWrite.PartsOrderFor),
        .. trial.Group is { } group ? NavmeshUndoWrite.OrdersFor(group) : [],
    ];
}
