using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Writes "removed" overrides into the patch: Initially Disabled, moved to Z = -30000 (X/Y kept),
/// and an existing Enable Parent (which would override the flag) replaced by the player with
/// "set enable state to opposite of parent", as xEdit's "Undelete and Disable References" does.
/// Not thread-safe.
/// </summary>
internal sealed class ObjectRemover(PlacedOverrideWriter overrides)
{
    /// <summary>Standard "safe disable" depth.</summary>
    private const float RemovedZ = -30000f;

    /// <summary>Always enabled, so "opposite of parent" keeps the object disabled.</summary>
    private static readonly FormKey PlayerRef = FormKey.Factory("000014:Skyrim.esm");

    /// <returns>True when an Enable Parent was replaced.</returns>
    public bool Disable(IPlacedGetter record, TargetLocation location)
    {
        var placed = overrides.GetOrAddOverride(record, location);

        placed.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
        if (placed.Placement is { } placement)
        {
            placement.Position = new P3Float(placement.Position.X, placement.Position.Y, RemovedZ);
        }

        if (placed.EnableParent == null) return false;
        placed.EnableParent = new EnableParent
        {
            Reference = new FormLink<IPlacedGetter>(PlayerRef),
            Flags = EnableParent.Flag.SetEnableStateToOppositeOfParent,
        };
        return true;
    }
}
