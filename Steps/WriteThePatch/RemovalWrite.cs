using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How a removed object is written, as xEdit's "Undelete and Disable References" does: Initially Disabled; a
/// non-persistent object is also moved to Z = -30000 (X/Y kept), while a persistent one (map markers among them)
/// stays where it is; and an existing Enable Parent (which would override the flag) is replaced by the player with
/// "set enable state to opposite of parent".
/// </summary>
/// <remarks>
/// The Enable Parent is replaced rather than only removed, so the object also stays disabled in a save that already
/// stored its enable state. Dropping the old parent changes only the removed object itself: a parent that is a target
/// object is in its linked group and removed with it, and enable children of the removed object are either in that
/// group too or, being another mod's objects, would have kept it.
/// </remarks>
internal static class RemovalWrite
{
    /// <summary>Standard "safe disable" depth.</summary>
    private const float RemovedZ = -30000f;

    /// <summary>Always enabled, so "opposite of parent" keeps the object disabled.</summary>
    private static readonly RecordKey PlayerRef = new(new PluginName("Skyrim.esm"), 0x14);

    public static IEnumerable<WriteOrder> OrdersFor(TargetObject target, bool hasEnableParent)
    {
        yield return new SetInitiallyDisabled(target.Key);
        if (!target.IsPersistent) yield return new SetPosition(target.Key, target.Transform.Position with { Z = RemovedZ });
        if (hasEnableParent) yield return new SetEnableParent(target.Key, PlayerRef, Opposite: true);
    }
}
