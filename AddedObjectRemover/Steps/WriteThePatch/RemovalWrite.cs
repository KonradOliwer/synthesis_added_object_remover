using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How a removed object is written: Initially Disabled, moved to Z = -30000 (X/Y kept), and an existing
/// Enable Parent (which would override the flag) replaced by the player with "set enable state to opposite
/// of parent", as xEdit's "Undelete and Disable References" does.
/// </summary>
internal static class RemovalWrite
{
    /// <summary>Standard "safe disable" depth.</summary>
    private const float RemovedZ = -30000f;

    /// <summary>Always enabled, so "opposite of parent" keeps the object disabled.</summary>
    private static readonly RecordKey PlayerRef = new(new PluginName("Skyrim.esm"), 0x14);

    public static IEnumerable<WriteOrder> OrdersFor(TargetObject target, bool hasEnableParent)
    {
        yield return new SetInitiallyDisabled(target.Key);
        var position = target.Transform.Position;
        yield return new SetPosition(target.Key, position with { Z = RemovedZ });
        if (hasEnableParent) yield return new SetEnableParent(target.Key, PlayerRef, Opposite: true);
    }
}
