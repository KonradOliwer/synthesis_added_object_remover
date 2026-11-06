using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The records the cleaned mod changed that share their grid with another of their kind: two exterior cell records at one grid of a
/// worldspace, or two LAND records in one cell. Which of them the game shows is not known, so neither goes back.
/// </summary>
internal static class SharedGrids
{
    public static IReadOnlySet<RecordKey> Of(CollectedObjects collected)
    {
        List<CellChangedByCleanedMod> exterior = [.. collected.ChangedCells.Where(cell => cell.Grid is not null)];
        var sharedGrids = KeyedGroups.CountBy(exterior, GridOf, EqualityComparer<(RecordKey, (int, int))>.Default)
            .Where(grid => grid.Value > 1)
            .Select(grid => grid.Key)
            .ToHashSet();
        var sharedCells = CellsWithSeveral(collected.ChangedLands);
        return exterior.Where(cell => sharedGrids.Contains(GridOf(cell))).Select(cell => cell.Cell)
            .Concat(collected.ChangedLands.Where(land => sharedCells.Contains(land.Cell)).Select(land => land.LandKey))
            .ToHashSet();
    }

    /// <summary>The cells that hold two or more of the LAND records.</summary>
    public static IReadOnlySet<ExteriorCell> CellsWithSeveral(IEnumerable<LandChangedByCleanedMod> lands) =>
        KeyedGroups.CountBy(lands, land => land.Cell, EqualityComparer<ExteriorCell>.Default).Where(cell => cell.Value > 1).Select(cell => cell.Key).ToHashSet();

    private static (RecordKey Space, (int X, int Y) Grid) GridOf(CellChangedByCleanedMod cell) => (cell.SpaceKey, cell.Grid!.Value);
}
