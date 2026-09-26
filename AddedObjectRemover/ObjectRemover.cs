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
internal sealed class ObjectRemover(ISkyrimMod patchMod)
{
    /// <summary>Standard "safe disable" depth.</summary>
    private const float RemovedZ = -30000f;

    /// <summary>Always enabled, so "opposite of parent" keeps the object disabled.</summary>
    private static readonly FormKey PlayerRef = FormKey.Factory("000014:Skyrim.esm");

    /// <summary>Per overridden child list (by reference): its records by FormKey.</summary>
    private readonly Dictionary<object, Dictionary<FormKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <returns>True when an Enable Parent was replaced.</returns>
    public bool Disable(IPlacedGetter record, TargetLocation location)
    {
        var placed = GetOrAddOverride(record, location);

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

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the winning cell (without its children),
    /// then add a copy of the winning record to the child list it was found in.
    /// </summary>
    private IPlaced GetOrAddOverride(IPlacedGetter record, TargetLocation location)
    {
        var cell = location.WinningCell.GetOrAddAsOverride(patchMod);
        var list = location.InPersistentList ? cell.Persistent : cell.Temporary;
        if (!_placedByList.TryGetValue(list, out var byFormKey))
        {
            byFormKey = new Dictionary<FormKey, IPlaced>();
            foreach (var existing in list) byFormKey.TryAdd(existing.FormKey, existing);
            _placedByList[list] = byFormKey;
        }

        if (!byFormKey.TryGetValue(record.FormKey, out var placed))
        {
            placed = (IPlaced)record.DeepCopy();
            list.Add(placed);
            byFormKey[record.FormKey] = placed;
        }
        return placed;
    }
}
