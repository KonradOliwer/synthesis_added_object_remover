using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The records of other plugins with no part to undo and some part left to a later plugin or the patch.
/// </summary>
internal sealed record HeldByLaterPlugins(
    ImmutableArray<RecordChangedByCleanedMod> Objects, ImmutableArray<CellChangedByCleanedMod> Cells, ImmutableArray<LandChangedByCleanedMod> Lands)
{
    public bool IsEmpty => Objects.IsEmpty && Cells.IsEmpty && Lands.IsEmpty;

    public static HeldByLaterPlugins In(CollectedObjects world) => new(
        [.. world.ChangedRecords.Where(change => !change.HasPartsToUndo && change.HasPartsLeftToLaterPlugins)],
        [.. world.ChangedCells.Where(cell => !cell.HasPartsToUndo && cell.HasPartsLeftToLaterPlugins)],
        [.. world.ChangedLands.Where(land => !land.HasPartsToUndo && land.LeftToLater.HasParts)]);
}
