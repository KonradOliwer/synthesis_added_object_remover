using System.Numerics;
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
    public static IEnumerable<WriteOrder> OrdersFor(TargetObject target) =>
        OrdersFor(target.Key, target.Transform.Position, target.IsPersistent, target.HasEnableParent);

    /// <remarks>Another mod's object is removed only when it is not persistent and has no enable parent.</remarks>
    public static IEnumerable<WriteOrder> OrdersFor(OtherObject other) =>
        OrdersFor(other.Key, other.Position, other.Roles.HasFlag(ReferenceRoles.Persistent), other.Roles.HasFlag(ReferenceRoles.HasEnableParent));

    private static IEnumerable<WriteOrder> OrdersFor(RecordKey key, Vector3 position, bool isPersistent, bool hasEnableParent)
    {
        yield return new SetInitiallyDisabled(key);
        if (!isPersistent) yield return new SetPosition(key, position with { Z = RemovalConvention.RemovedZ });
        if (hasEnableParent) yield return new SetEnableParent(key, RemovalConvention.PlayerRef, RemovalConvention.OppositeOfParent);
    }
}
