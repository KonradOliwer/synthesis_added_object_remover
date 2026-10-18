using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>Names where each target object is, from the winning cells of the target spaces. Built once and only read.</summary>
/// <param name="Cells">Every winning cell of the target spaces, by key.</param>
/// <param name="GridCells">The exterior cells by worldspace and grid position; of two cells at one position (broken data), the first in key order.</param>
internal sealed record TargetAreaNames(
    IReadOnlyDictionary<RecordKey, CellAreaFact> Cells,
    IReadOnlyDictionary<(RecordKey Space, CellGridPoint Grid), RecordKey> GridCells)
{
    /// <summary>How many cells away, in each grid direction, a cell without a Location borrows the nearest cell's Location.</summary>
    private const int NearbyCellReach = 3;

    public static TargetAreaNames Of(IReadOnlyDictionary<RecordKey, CellAreaFact> cells) =>
        new(cells, cells
            .Where(entry => entry.Value.Grid is not null)
            .GroupBy(entry => (entry.Value.Space, entry.Value.Grid!.Value))
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Key).Min(RecordKeyOrder.Comparer)));

    /// <param name="cellKey">The cell whose child list holds the object; for an interior, the space itself.</param>
    /// <remarks>An object in a worldspace's persistent cell is named by the grid cell its position falls in.</remarks>
    public TargetArea Name(SpaceFact space, RecordKey cellKey, Vector3 position)
    {
        var cell = Cells[cellKey];
        if (space.Kind == SpaceKind.Interior) return new TargetArea(cell.Location?.Hold, cell.Name, PlaceSource.CellName, Grid: null);

        var grid = cell.Grid ?? new CellGridPoint(ExteriorGrid.CellIndex(position.X), ExteriorGrid.CellIndex(position.Y));
        return NameExterior(space.Key, cell.Grid is null ? FindCellAt(space.Key, grid) : cell, grid);
    }

    /// <param name="cell">The cell at the grid position; null when the load order has none there.</param>
    private TargetArea NameExterior(RecordKey space, CellAreaFact? cell, CellGridPoint grid)
    {
        if (cell?.Location is { } own) return new TargetArea(own.Hold, own.Place, PlaceSource.OwnLocation, grid);
        var nearby = FindNearbyLocation(space, grid);
        if (cell?.Name is { } name) return new TargetArea(nearby?.Hold, name, PlaceSource.CellName, grid);
        return nearby is { } borrowed
            ? new TargetArea(borrowed.Hold, borrowed.Place, PlaceSource.NearbyLocation, grid)
            : new TargetArea(Hold: null, Place: null, PlaceSource.GridOnly, grid);
    }

    /// <returns>The Location of the nearest cell within reach that has one; of equally near cells, the first in key order.</returns>
    private LocationNames? FindNearbyLocation(RecordKey space, CellGridPoint grid)
    {
        var offsets = Enumerable.Range(-NearbyCellReach, 2 * NearbyCellReach + 1).ToList();
        return offsets
            .SelectMany(dx => offsets.Select(dy => (Distance: dx * dx + dy * dy, Point: new CellGridPoint(grid.X + dx, grid.Y + dy))))
            .Select(candidate => (candidate.Distance, Cell: FindCellKeyAt(space, candidate.Point)))
            .Where(candidate => candidate.Cell is { } key && Cells[key].Location is not null)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Cell!.Value, RecordKeyOrder.Comparer)
            .Select(candidate => Cells[candidate.Cell!.Value].Location)
            .FirstOrDefault();
    }

    private CellAreaFact? FindCellAt(RecordKey space, CellGridPoint grid) => FindCellKeyAt(space, grid) is { } key ? Cells[key] : null;

    private RecordKey? FindCellKeyAt(RecordKey space, CellGridPoint grid) => GridCells.TryGetValue((space, grid), out var key) ? key : null;
}
