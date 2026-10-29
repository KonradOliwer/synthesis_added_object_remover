using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch;

/// <summary>
/// How a restored object is written: an override of the record the game shows now, with no Initially Disabled, no sinking and no
/// change of its enable parent. When the earlier version has the same base object, only the position, rotation and scale go back
/// (links, scripts and flags stay as the cleaned mod and earlier patchers left them). Otherwise the whole earlier record is the
/// override, because the fields that go with the base object must match it.
/// </summary>
internal static class RestoreWrite
{
    public static IEnumerable<WriteOrder> OrdersFor(TargetObject target, EarlierVersion earlier)
    {
        if (GoesBackToEarlierCell(target, earlier)) yield return new PlaceInCell(target.Key, earlier.Cell.Key);

        yield return earlier.RestoresWholeRecord
            ? new CopyVersion(target.Key)
            : new SetPlacement(target.Key, earlier.Placement.Position, earlier.Placement.Rotation, earlier.StoredScale);
    }

    /// <remarks>
    /// A persistent object stays in the cell of the version the game shows now. The earlier version stands in the object's own
    /// space, and an interior is its own cell (the target has none), so only an object of a worldspace can go back to another cell.
    /// </remarks>
    private static bool GoesBackToEarlierCell(TargetObject target, EarlierVersion earlier) =>
        !target.IsPersistent && !earlier.IsPersistent && target.Cell is { } cell && cell.Key != earlier.Cell.Key;
}
