using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Where a cell lies: its worldspace and grid, or itself for an interior.</summary>
internal static class CellPlaces
{
    /// <returns>Null for an interior.</returns>
    public static IWorldspaceGetter? WorldspaceOf(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        for (var current = cellContext.Parent; current != null; current = current.Parent)
        {
            if (current.Record is IWorldspaceGetter worldspace) return worldspace;
        }
        return null;
    }

    /// <param name="spaceKey">The cell's worldspace, or the cell itself for an interior.</param>
    public static NavmeshPlace PlaceOf(ICellGetter cell, RecordKey cellKey, RecordKey spaceKey) =>
        spaceKey != cellKey && cell.Grid is { } grid ? new NavmeshPlace(spaceKey, new CellGridPoint(grid.Point.X, grid.Point.Y)) : new NavmeshPlace(cellKey, null);
}
