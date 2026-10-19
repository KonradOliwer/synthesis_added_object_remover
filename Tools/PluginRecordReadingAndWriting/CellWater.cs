namespace AddedObjectRemover;

/// <summary>Every plugin's own water fields of the target worldspaces' exterior cells and of the worldspaces that supply their default water.</summary>
/// <param name="cells">Every plugin's copy of each cell's water fields, highest priority first; null when the water was not asked for.</param>
/// <param name="worldspaces">Every plugin's copy of each water-supplying worldspace's default water height, in load order.</param>
/// <param name="waterWorldspaces">Target worldspace -> the worldspace whose default water it uses (itself, or a parent whose water data it uses).</param>
internal sealed class CellWater(
    IReadOnlyDictionary<ExteriorCell, List<CellWaterCopy>>? cells,
    IReadOnlyDictionary<RecordKey, List<WorldspaceWaterCopy>> worldspaces,
    IReadOnlyDictionary<RecordKey, RecordKey> waterWorldspaces)
{
    /// <param name="cell">The cell in the worldspace the target's objects are in.</param>
    /// <returns>Each part empty where no plugin has a copy of it.</returns>
    public WaterCopies ReadCopies(ExteriorCell cell)
    {
        if (cells is null) throw new InvalidOperationException("The water copies were not read; ask for the water in the read scope.");
        var worldspaceCopies = waterWorldspaces.TryGetValue(cell.WorldspaceKey, out var supplier) ? worldspaces.GetValueOrDefault(supplier) : null;
        return new WaterCopies(
            [.. (cells.GetValueOrDefault(cell) ?? []).AsEnumerable().Reverse()],
            [.. worldspaceCopies ?? []]);
    }
}
