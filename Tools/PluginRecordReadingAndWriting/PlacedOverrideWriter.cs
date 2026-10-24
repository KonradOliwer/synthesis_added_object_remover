using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Gives the patch's override of a target placed record, creating it on first use. Not thread-safe.
/// </summary>
internal sealed class PlacedOverrideWriter(SynthesisWriteCache synthesis, ISkyrimMod patchMod)
{
    /// <summary>The patch's cell overrides this writer gave out, by cell FormKey.</summary>
    private readonly Dictionary<FormKey, ICell> _cells = [];

    /// <summary>Per overridden child list (by reference): its records by FormKey.</summary>
    private readonly Dictionary<object, Dictionary<FormKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the cell (without its children), then add a copy of the
    /// winning record to the child list of the given kind.
    /// </summary>
    /// <param name="cellKey">The cell the record was found in, or the cell it is moved into.</param>
    public IPlaced GetOrAddOverride(IPlacedGetter record, FormKey cellKey, bool inPersistentList)
    {
        var cell = GetOrAddCellOverride(cellKey);
        var list = inPersistentList ? cell.Persistent : cell.Temporary;
        if (!_placedByList.TryGetValue(list, out var byKey))
        {
            byKey = new Dictionary<FormKey, IPlaced>();
            foreach (var existing in list) byKey.TryAdd(existing.FormKey, existing);
            _placedByList[list] = byKey;
        }

        if (!byKey.TryGetValue(record.FormKey, out var placed))
        {
            placed = (IPlaced)record.DeepCopy();
            list.Add(placed);
            byKey[record.FormKey] = placed;
        }
        return placed;
    }

    /// <remarks>
    /// The cell's context comes from the Synthesis cache, so an exterior cell's worldspace is copied from a plugin above the patch,
    /// and a cell copy an earlier run wrote is reused.
    /// </remarks>
    private ICell GetOrAddCellOverride(FormKey cellKey)
    {
        if (_cells.TryGetValue(cellKey, out var cell)) return cell;
        cell = synthesis.Cache.ResolveContext<ICell, ICellGetter>(cellKey).GetOrAddAsOverride(patchMod);
        _cells[cellKey] = cell;
        return cell;
    }
}
