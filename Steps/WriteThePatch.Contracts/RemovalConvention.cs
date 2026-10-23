namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>
/// The game's convention for switching a placed object off for good, used by xEdit, other mods and this patch: the object is
/// Initially Disabled, and an object with an enable parent gets the player as enable parent with its enable state set to the
/// opposite of the player's. The player is always enabled, so the object stays disabled, also in a save that stored its enable
/// state. A non-persistent removed object is also moved down to <see cref="RemovedZ"/>; that depth is a side effect, never a
/// sign of removal.
/// </summary>
public static class RemovalConvention
{
    /// <summary>The player reference.</summary>
    public static readonly RecordKey PlayerRef = new(new PluginName("Skyrim.esm"), 0x14);

    /// <summary>The removed object's enable state is the opposite of its enable parent's.</summary>
    public const bool OppositeOfParent = true;

    /// <summary>Standard "safe disable" depth.</summary>
    public const float RemovedZ = -30000f;
}
