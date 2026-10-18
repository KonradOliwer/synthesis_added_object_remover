using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A winning cell record of a target space.</summary>
internal sealed record SpaceCell(RecordKey Key, SpaceFact Space, ICellGetter Record);

/// <summary>
/// Reads where each winning cell of the target spaces is, after the scan and in parallel passes: the cells' own fields, then the
/// top Location of each worldspace once, then each Location chain once per (Location, worldspace top). Every pass maps its
/// items to results by index, so the facts do not depend on the number of workers.
/// </summary>
internal static class CellAreaReader
{
    private sealed record CellFields(CellGridPoint? Grid, string? Name, FormKey? Location, FormKey? Worldspace);

    public static IReadOnlyDictionary<RecordKey, CellAreaFact> Read(IReadOnlyList<SpaceCell> cells, LocationNameReader locations, Execution execution)
    {
        var fields = ParallelMap.Run(execution, cells.Count, item => ReadFields(cells[item]), ParallelMap.AutomaticRangeSize);
        var tops = ReadOncePerKey(fields.Select(cell => cell.Worldspace).OfType<FormKey>(), worldspace => locations.FindWorldspaceTop(worldspace), execution);
        var chains = ReadOncePerKey(
            fields.Where(cell => cell.Location is not null).Select(cell => ChainKey(cell, tops)),
            chain => locations.Read(chain.Location, chain.Top),
            execution);
        return Enumerable.Range(0, cells.Count).ToDictionary(item => cells[item].Key, item => CreateFact(cells[item], fields[item], tops, chains));
    }

    private static CellAreaFact CreateFact(
        SpaceCell cell, CellFields fields, IReadOnlyDictionary<FormKey, FormKey?> tops, IReadOnlyDictionary<(FormKey, FormKey?), LocationNames?> chains) =>
        new(cell.Space.Key, fields.Grid, fields.Name, fields.Location is null ? null : chains[ChainKey(fields, tops)]);

    private static CellFields ReadFields(SpaceCell cell)
    {
        var record = cell.Record;
        var interior = cell.Space.Kind == SpaceKind.Interior;
        CellGridPoint? grid = !interior && record.Grid is { } cellGrid ? new CellGridPoint(cellGrid.Point.X, cellGrid.Point.Y) : null;
        return new CellFields(grid, RecordDisplayNames.Of(record.Name, record.EditorID), record.Location.FormKeyNullable, interior ? null : cell.Space.Key.ToFormKey());
    }

    private static (FormKey Location, FormKey? Top) ChainKey(CellFields cell, IReadOnlyDictionary<FormKey, FormKey?> tops) =>
        (cell.Location!.Value, cell.Worldspace is { } worldspace ? tops[worldspace] : null);

    /// <summary>Each Location chain or worldspace top is the same for every cell that shares it, so it is read once.</summary>
    private static Dictionary<TKey, TValue> ReadOncePerKey<TKey, TValue>(IEnumerable<TKey> keys, Func<TKey, TValue> read, Execution execution)
        where TKey : notnull
    {
        var distinct = keys.Distinct().ToArray();
        var values = ParallelMap.Run(execution, distinct.Length, item => read(distinct[item]), ParallelMap.OneItemPerRange);
        return distinct.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second);
    }
}
