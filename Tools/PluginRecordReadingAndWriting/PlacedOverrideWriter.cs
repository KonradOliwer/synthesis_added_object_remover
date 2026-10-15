using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Gives the patch's override of a target placed record, creating it on first use. Not thread-safe.
/// </summary>
internal sealed class PlacedOverrideWriter(ISkyrimMod patchMod, PluginNames names)
{
    /// <summary>Per overridden child list (by reference): its records by key.</summary>
    private readonly Dictionary<object, Dictionary<RecordKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the winning cell (without its children),
    /// then add a copy of the winning record to the child list it was found in.
    /// </summary>
    public IPlaced GetOrAddOverride(IPlacedGetter record, PlacedRecordLocation location)
    {
        var cell = location.WinningCell.GetOrAddAsOverride(patchMod);
        var list = location.InPersistentList ? cell.Persistent : cell.Temporary;
        if (!_placedByList.TryGetValue(list, out var byKey))
        {
            byKey = new Dictionary<RecordKey, IPlaced>();
            foreach (var existing in list) byKey.TryAdd(names.KeyOf(existing.FormKey), existing);
            _placedByList[list] = byKey;
        }

        var key = names.KeyOf(record.FormKey);
        if (!byKey.TryGetValue(key, out var placed))
        {
            placed = (IPlaced)record.DeepCopy();
            list.Add(placed);
            byKey[key] = placed;
        }
        return placed;
    }
}
