using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

internal static class HomeCells
{
    /// <summary>
    /// The exterior cell whose list holds the reference, or for a reference of a worldspace's
    /// persistent cell the cell it stands in; null for an interior reference.
    /// </summary>
    public static CellArea? Find(TargetObject target, IPluginRecords plugin)
    {
        if (target.Cell == null) return null;
        if (plugin.ReadHomeCellGrid(target.Key) is { } grid) return CellArea.Single(grid.X, grid.Y);
        var position = target.Transform.Position;
        return CellArea.Single(ExteriorGrid.CellIndex(position.X), ExteriorGrid.CellIndex(position.Y));
    }
}
