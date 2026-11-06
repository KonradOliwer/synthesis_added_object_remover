using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The ground as it is, with the heights of some cells of land replaced. Thread-safe.</summary>
/// <param name="now">The ground as it is.</param>
/// <param name="replaced">The heights of each cell replaced.</param>
internal sealed class GroundWithCellsReplaced(ITerrainHeights now, IReadOnlyDictionary<ExteriorCell, float[]> replaced, Func<RecordKey, RecordKey?> findLandWorldspace) : ITerrainHeights
{
    private readonly TerrainHeights _replaced = new(cell => replaced.GetValueOrDefault(cell), findLandWorldspace);

    public bool HasTerrain(RecordKey spaceKey) => now.HasTerrain(spaceKey);

    public bool TryGetHeight(RecordKey worldspaceKey, Vector2 position, out float height) =>
        IsReplaced(worldspaceKey, ExteriorGrid.CellIndex(position.X), ExteriorGrid.CellIndex(position.Y))
            ? _replaced.TryGetHeight(worldspaceKey, position, out height)
            : now.TryGetHeight(worldspaceKey, position, out height);

    public bool HasHeights(RecordKey worldspaceKey, int cellX, int cellY) => IsReplaced(worldspaceKey, cellX, cellY) || now.HasHeights(worldspaceKey, cellX, cellY);

    private bool IsReplaced(RecordKey worldspaceKey, int cellX, int cellY) =>
        findLandWorldspace(worldspaceKey) is { } land && replaced.ContainsKey(new ExteriorCell(land, cellX, cellY));
}
