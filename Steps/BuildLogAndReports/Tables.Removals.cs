using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> RemovalsHeader =
    [
        "formKey", "editorId", "base", "reason", "worldspace", "hold", "place", "cellFormKey", "cellEditorId", "gridX", "gridY", "x", "y", "z",
    ];

    /// <summary>One row per removal, ordered by worldspace, hold and place names (ordinal), then by record key.</summary>
    /// <remarks>An interior's cell is the space itself, and it has no grid.</remarks>
    public static CsvTable BuildRemovalsTable(CollectedObjects world, IEnumerable<RemovedObject> removals, ReportContext context) =>
        new(ReportFileNames.RemovalsFileName, RemovalsHeader, [.. InAreaOrder(world, removals).Select(removal => RemovalRow(world, removal, context))]);

    private static IEnumerable<RemovedObject> InAreaOrder(CollectedObjects world, IEnumerable<RemovedObject> removals)
    {
        TargetObject TargetOf(RemovedObject removal) => world.Targets[removal.TargetIndex];
        SpaceFact SpaceOf(RemovedObject removal) => world.Spaces[TargetOf(removal).SpaceKey];
        return removals
            .OrderBy(removal => AreaText.Worldspace(SpaceOf(removal)), StringComparer.Ordinal)
            .ThenBy(removal => AreaText.Hold(TargetOf(removal).Area), StringComparer.Ordinal)
            .ThenBy(removal => AreaText.Place(TargetOf(removal).Area, SpaceOf(removal)), StringComparer.Ordinal)
            .ThenBy(removal => TargetOf(removal).Key, RecordKeyOrder.Comparer);
    }

    private static ImmutableArray<string> RemovalRow(CollectedObjects world, RemovedObject removal, ReportContext context)
    {
        var target = world.Targets[removal.TargetIndex];
        var space = world.Spaces[target.SpaceKey];
        var grid = target.Area.Grid;
        return CsvRow.Of(
        [
            Quote(target.Key.ToString()),
            OptionalText(target.EditorId),
            Quote(RecordNames.DescribeBase(context.Bases, target.Base)),
            Quote(Describe.RemovalReason(world, removal)),
            Quote(AreaText.Worldspace(space)),
            Quote(AreaText.Hold(target.Area)),
            Quote(AreaText.Place(target.Area, space)),
            Quote((target.Cell?.Key ?? space.Key).ToString()),
            OptionalText(target.Cell is { } cell ? cell.EditorId : space.EditorId),
            OptionalText(grid is { } column ? Int(column.X) : null),
            OptionalText(grid is { } row ? Int(row.Y) : null),
            .. Numbers(target.Transform.Position),
        ]);
    }
}
