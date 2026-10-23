using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How a removed object is written, in the game's convention (<see cref="RemovalConvention"/>), as xEdit's "Undelete and
/// Disable References" does: Initially Disabled; a non-persistent object is also moved down to the removed depth (X/Y kept),
/// while a persistent one (map markers among them) stays where it is; and an existing Enable Parent (which would override the
/// flag) is replaced by the player with "set enable state to opposite of parent".
/// </summary>
/// <remarks>
/// The Enable Parent is replaced rather than only removed, so the object also stays disabled in a save that already
/// stored its enable state. Dropping the old parent changes only the removed object itself: a parent that is a target
/// object is in its linked group and removed with it, and enable children of the removed object are either in that
/// group too or, being another mod's objects, would have kept it.
/// </remarks>
internal static class RemovalWrite
{
    public static IEnumerable<WriteOrder> OrdersFor(TargetObject target)
    {
        yield return new SetInitiallyDisabled(target.Key);
        if (!target.IsPersistent) yield return new SetPosition(target.Key, target.Transform.Position with { Z = RemovalConvention.RemovedZ });
        if (target.HasEnableParent) yield return new SetEnableParent(target.Key, RemovalConvention.PlayerRef, RemovalConvention.OppositeOfParent);
    }
}
