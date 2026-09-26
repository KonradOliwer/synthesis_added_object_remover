using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Gives the patch's override of a target placed record, creating it on first use. Not thread-safe.
/// </summary>
internal sealed class PlacedOverrideWriter(ISkyrimMod patchMod)
{
    /// <summary>Per overridden child list (by reference): its records by FormKey.</summary>
    private readonly Dictionary<object, Dictionary<FormKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the winning cell (without its children),
    /// then add a copy of the winning record to the child list it was found in.
    /// </summary>
    public IPlaced GetOrAddOverride(IPlacedGetter record, TargetLocation location)
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
