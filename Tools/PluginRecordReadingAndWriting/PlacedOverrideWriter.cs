using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Gives the patch's override of a target placed record, creating it on first use, and keeps it in one cell: it never holds the
/// record twice. Not thread-safe.
/// </summary>
internal sealed class PlacedOverrideWriter(SynthesisWriteCache synthesis, ISkyrimMod patchMod)
{
    /// <summary>The patch's cell overrides this writer gave out, by cell FormKey.</summary>
    private readonly Dictionary<FormKey, ICell> _cells = [];

    /// <summary>Per overridden child list (by reference): its records by FormKey.</summary>
    private readonly Dictionary<object, Dictionary<FormKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <summary>The cell each record this writer moved now stands in, in its temporary children.</summary>
    private readonly Dictionary<FormKey, FormKey> _cellOfMoved = [];

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the cell (without its children), then add a copy of the
    /// winning record to the child list of the given kind.
    /// </summary>
    /// <param name="cellKey">The cell the record was found in.</param>
    public IPlaced GetOrAddOverride(IPlacedGetter record, FormKey cellKey, bool inPersistentList)
    {
        var (list, byKey) = ChildListOfRecord(record.FormKey, cellKey, inPersistentList);
        return byKey.TryGetValue(record.FormKey, out var placed) ? placed : Add(list, byKey, (IPlaced)record.DeepCopy());
    }

    /// <summary>
    /// The record stands in the temporary children of <paramref name="toCellKey"/> and nowhere else in the patch. The cell it
    /// leaves is not overridden for it: only a copy that cell already holds in the patch, in the list the record was found in, is
    /// taken out.
    /// </summary>
    /// <param name="fromCellKey">The cell the record was found in.</param>
    /// <param name="fromPersistentList">Whether the record was found in the persistent children of that cell.</param>
    public void MoveToCell(IPlacedGetter record, FormKey fromCellKey, bool fromPersistentList, FormKey toCellKey)
    {
        if (_cellOfMoved.ContainsKey(record.FormKey))
            throw new InvalidOperationException($"Record {record.FormKey} was already moved to another cell by this writer.");
        var held = PatchHoldsCell(fromCellKey) ? TakeOutOfList(record.FormKey, fromCellKey, fromPersistentList) : null;
        var (list, byKey) = ChildListOf(toCellKey, inPersistentList: false);
        Add(list, byKey, held ?? (IPlaced)record.DeepCopy());
        _cellOfMoved[record.FormKey] = toCellKey;
    }

    /// <summary>
    /// The patch's record becomes a copy of <paramref name="version"/>, in the child list it stands in. The copy's Persistent flag
    /// follows that list, so the flag and the list agree whatever the version had.
    /// </summary>
    public void ReplaceWithCopyOf(IPlacedGetter version, FormKey cellKey, bool inPersistentList)
    {
        var persistent = StandsInPersistentList(version.FormKey, inPersistentList);
        var (list, byKey) = ChildListOfRecord(version.FormKey, cellKey, inPersistentList);
        if (byKey.Remove(version.FormKey, out var replaced)) list.Remove(replaced);
        var copy = (IPlaced)version.DeepCopy();
        copy.SetPersistent(persistent);
        Add(list, byKey, copy);
    }

    private (IList<IPlaced> List, Dictionary<FormKey, IPlaced> ByKey) ChildListOfRecord(FormKey record, FormKey foundInCellKey, bool foundInPersistentList) =>
        _cellOfMoved.TryGetValue(record, out var movedTo) ? ChildListOf(movedTo, inPersistentList: false) : ChildListOf(foundInCellKey, foundInPersistentList);

    private bool StandsInPersistentList(FormKey record, bool foundInPersistentList) =>
        !_cellOfMoved.ContainsKey(record) && foundInPersistentList;

    private (IList<IPlaced> List, Dictionary<FormKey, IPlaced> ByKey) ChildListOf(FormKey cellKey, bool inPersistentList)
    {
        var cell = GetOrAddCellOverride(cellKey);
        IList<IPlaced> list = inPersistentList ? cell.Persistent : cell.Temporary;
        if (!_placedByList.TryGetValue(list, out var byKey))
        {
            byKey = new Dictionary<FormKey, IPlaced>();
            foreach (var existing in list) byKey.TryAdd(existing.FormKey, existing);
            _placedByList[list] = byKey;
        }
        return (list, byKey);
    }

    private static IPlaced Add(IList<IPlaced> list, Dictionary<FormKey, IPlaced> byKey, IPlaced placed)
    {
        list.Add(placed);
        byKey[placed.FormKey] = placed;
        return placed;
    }

    private IPlaced? TakeOutOfList(FormKey record, FormKey cellKey, bool inPersistentList)
    {
        var (list, byKey) = ChildListOf(cellKey, inPersistentList);
        if (!byKey.Remove(record, out var held)) return null;
        list.Remove(held);
        return held;
    }

    private bool PatchHoldsCell(FormKey cellKey) =>
        _cells.ContainsKey(cellKey) || (synthesis.Cache.TryResolveContext<ICell, ICellGetter>(cellKey, out var context) && context.ModKey == patchMod.ModKey);

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
