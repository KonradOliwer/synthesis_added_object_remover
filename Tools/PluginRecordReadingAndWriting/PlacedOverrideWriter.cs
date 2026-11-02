using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Gives the patch's override of a placed record, creating it on first use, and keeps it in one child list: it never holds
/// the record twice. Not thread-safe.
/// </summary>
/// <param name="parts">The table a part is copied through.</param>
internal sealed class PlacedOverrideWriter(CellOverrides cells, PlacedRecordParts parts)
{
    /// <summary>Per overridden child list (by reference): its records by FormKey.</summary>
    private readonly Dictionary<object, Dictionary<FormKey, IPlaced>> _placedByList = new(ReferenceEqualityComparer.Instance);

    /// <summary>The child list each record this writer moved now stands in.</summary>
    private readonly Dictionary<FormKey, (FormKey Cell, bool InPersistentList)> _listOfMoved = [];

    /// <summary>
    /// Mirrors Mutagen's placed-record context: override the cell (without its children), then add a copy of the
    /// winning record to the child list of the given kind.
    /// </summary>
    /// <param name="cellKey">The cell the record was found in.</param>
    public IPlaced GetOrAddOverride(IPlacedGetter record, FormKey cellKey, bool inPersistentList)
    {
        var (list, byKey) = ChildListOfRecord(record.FormKey, cellKey, inPersistentList);
        return byKey.TryGetValue(record.FormKey, out var placed) ? placed : Add(list, byKey, CopyOf(record));
    }

    /// <summary>
    /// The record stands in the given child list of <paramref name="toCellKey"/> and nowhere else in the patch, with its Persistent
    /// flag following that list. The cell it leaves is not overridden for it: only a copy that cell already holds in the patch, in
    /// the list the record was found in, is taken out.
    /// </summary>
    /// <param name="fromCellKey">The cell the record was found in.</param>
    /// <param name="fromPersistentList">Whether the record was found in the persistent children of that cell.</param>
    public void MoveToList(IPlacedGetter record, FormKey fromCellKey, bool fromPersistentList, FormKey toCellKey, bool toPersistentList)
    {
        if (_listOfMoved.ContainsKey(record.FormKey))
            throw new InvalidOperationException($"Record {record.FormKey} was already moved to another list by this writer.");
        var held = cells.PatchHolds(fromCellKey) ? TakeOutOfList(record.FormKey, fromCellKey, fromPersistentList) : null;
        var (list, byKey) = ChildListOf(toCellKey, toPersistentList);
        var moved = held ?? CopyOf(record);
        moved.SetPersistent(toPersistentList);
        Add(list, byKey, moved);
        _listOfMoved[record.FormKey] = (toCellKey, toPersistentList);
    }

    /// <summary>
    /// The patch's record takes, in each of <paramref name="copied"/>, the values of <paramref name="earlier"/>; every other part keeps
    /// the value of the record the game shows now. A deleted record whose presence is copied becomes a whole copy of
    /// <paramref name="earlier"/>, because deleting dropped its fields, except for its door link: unless the door link is copied
    /// too, the copy keeps the deleted record's.
    /// </summary>
    /// <param name="cellKey">The cell the record was found in.</param>
    public void CopyEarlierParts(IPlacedGetter record, IPlacedGetter earlier, PlacedParts copied, FormKey cellKey, bool inPersistentList)
    {
        if (record.IsDeleted && copied.HasFlag(PlacedParts.Presence))
        {
            var copy = ReplaceWithCopyOf(earlier, cellKey, inPersistentList);
            if (!copied.HasFlag(PlacedParts.NavDoorLink)) parts.NavDoorLink.CopyInto(copy, record);
            return;
        }
        var placed = GetOrAddOverride(record, cellKey, inPersistentList);
        foreach (var (part, compare) in parts.Parts.Where(entry => copied.HasFlag(entry.Part)))
        {
            if (!RawByteFormat.AllowsCopy(compare, earlier, placed))
                throw new InvalidOperationException($"Part {part} of record {record.FormKey} holds bytes of another record format and cannot be copied.");
            compare.CopyInto(placed, earlier);
        }
    }

    /// <summary>
    /// The patch's record becomes a copy of <paramref name="version"/>, in the child list it stands in. The copy's Persistent flag
    /// follows that list, so the flag and the list agree whatever the version had.
    /// </summary>
    private IPlaced ReplaceWithCopyOf(IPlacedGetter version, FormKey cellKey, bool inPersistentList)
    {
        var persistent = StandsInPersistentList(version.FormKey, inPersistentList);
        var (list, byKey) = ChildListOfRecord(version.FormKey, cellKey, inPersistentList);
        if (byKey.Remove(version.FormKey, out var replaced)) list.Remove(replaced);
        var copy = CopyOf(version);
        copy.SetPersistent(persistent);
        return Add(list, byKey, copy);
    }

    private static IPlaced CopyOf(IPlacedGetter record) => (IPlaced)MutagenCalls.Read(() => record.DeepCopy());

    private (IList<IPlaced> List, Dictionary<FormKey, IPlaced> ByKey) ChildListOfRecord(FormKey record, FormKey foundInCellKey, bool foundInPersistentList) =>
        _listOfMoved.TryGetValue(record, out var movedTo)
            ? ChildListOf(movedTo.Cell, movedTo.InPersistentList)
            : ChildListOf(foundInCellKey, foundInPersistentList);

    private bool StandsInPersistentList(FormKey record, bool foundInPersistentList) =>
        _listOfMoved.TryGetValue(record, out var movedTo) ? movedTo.InPersistentList : foundInPersistentList;

    private (IList<IPlaced> List, Dictionary<FormKey, IPlaced> ByKey) ChildListOf(FormKey cellKey, bool inPersistentList)
    {
        var cell = cells.GetOrAdd(cellKey);
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
}
