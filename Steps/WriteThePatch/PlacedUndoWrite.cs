using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How another plugin's placed record goes back to the version the game shows without the cleaned mod: an override of the record
/// the game shows now in which only the parts the cleaned mod was the last to change take that version's values, so a part a later
/// plugin or an earlier patcher changed keeps its value.
/// </summary>
internal static class PlacedUndoWrite
{
    /// <param name="cellNow">The cell the record stands in now; null for an interior, whose cell is the space itself.</param>
    /// <param name="persistentNow">The record's own Persistent flag is set now.</param>
    public static IEnumerable<WriteOrder> OrdersFor(RecordKey key, CellFact? cellNow, bool persistentNow, EarlierVersion earlier)
    {
        if (earlier.GoesBackToEarlierList)
            yield return new PlaceInCell(key, earlier.Cell.Key, earlier.IsPersistent);
        else if (earlier.GoesBackToEarlierCell(cellNow, persistentNow))
            yield return new PlaceInCell(key, earlier.Cell.Key, InPersistentList: false);

        yield return new UndoPlacedParts(key, earlier.Undo);
    }
}
