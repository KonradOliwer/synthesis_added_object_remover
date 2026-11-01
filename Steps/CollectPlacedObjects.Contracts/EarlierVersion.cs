using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>The version of a placed object that the game shows without the cleaned mod.</summary>
/// <param name="Plugin">The plugin the version comes from.</param>
/// <param name="Placement">Where and how the object stands in that version, the scale normalized.</param>
/// <param name="Base">The version's base object; null when its base link is empty.</param>
/// <param name="Cell">The cell that plugin puts the record in.</param>
/// <param name="IsPersistent">The version's own Persistent flag is set.</param>
/// <param name="Undo">
/// The parts the cleaned mod is the last to change, which go back to this version's values; none when a version above the cleaned
/// mod's removes the record. With <see cref="PlacedParts.BaseAndData"/>, every field that goes with the base comes back with it.
/// </param>
/// <param name="LeftToLaterPlugins">The parts the cleaned mod changed and a later plugin, or the patch, changed again, each with the last of them; they stay.</param>
/// <param name="KeptForRecordFormat">The parts that would go back, but stay because this version stores them in another record format than the winner's.</param>
/// <param name="PersistenceGoesBack">
/// The cleaned mod is the last to change the record's Persistent flag, so the flag goes back to this version's with the child list
/// it stands in, when the presence goes back.
/// </param>
/// <param name="Reference">What the version says about the reference itself; it stands with these only when <see cref="PlacedParts.BaseAndData"/> goes back.</param>
/// <param name="DeletedCellRestoredInto">
/// The cell a restore of <see cref="Undo"/> writes the record into, when the final world deletes it; null when it does not. Nothing
/// is written into a deleted cell.
/// </param>
public sealed record EarlierVersion(
    PluginName Plugin,
    EulerPlacement Placement,
    BaseKey? Base,
    CellFact Cell,
    bool IsPersistent,
    PlacedParts Undo,
    ImmutableSortedDictionary<PlacedParts, PluginName> LeftToLaterPlugins,
    PlacedParts KeptForRecordFormat,
    bool PersistenceGoesBack,
    ReferenceFacts Reference,
    DeletedCell? DeletedCellRestoredInto)
{
    /// <summary>
    /// A restore puts the record back into the child list of this version: its presence goes back, such as for a record the cleaned
    /// mod deleted, unless a later plugin or an earlier patcher set its Persistent flag again, or moved it: its position then may lie
    /// in another cell than this version's, and a temporary record must stand in the cell that holds its position.
    /// </summary>
    public bool GoesBackToEarlierList => Undo.HasFlag(PlacedParts.Presence) && PersistenceGoesBack && !LeftToLaterPlugins.ContainsKey(PlacedParts.Position);

    /// <summary>
    /// A restore moves the record, as a temporary one, into this version's cell, since its position goes back. A persistent object
    /// stays in the cell of the version the game shows now. This version stands in the object's own space, and an interior is its own
    /// cell, so only an object of a worldspace can go back to another cell.
    /// </summary>
    /// <param name="cellNow">The cell the record stands in now; null for an interior, whose cell is the space itself.</param>
    /// <param name="persistentNow">The record's own Persistent flag is set now.</param>
    public bool GoesBackToEarlierCell(CellFact? cellNow, bool persistentNow) =>
        Undo.HasFlag(PlacedParts.Position) && !persistentNow && !IsPersistent && cellNow is { } cell && cell.Key != Cell.Key;
}
