using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <summary>Target x, y, z, distance moved and the surface moved onto.</summary>
    private const int MoveColumnCount = 5;

    private static readonly ImmutableArray<string> LeftBehindHeader =
    [
        .. LeftBehindObjectHeader("invisibleType"),
        "radius", "insideObject", "insideObjectPlugin",
        .. SectorHeader(),
        "decision", "reason",
        "movedToX", "movedToY", "movedToZ", "moveDistance", "movedOnto",
    ];

    /// <param name="kindColumn">The name of the column that holds the kind of the object.</param>
    private static ImmutableArray<string> LeftBehindObjectHeader(string kindColumn) =>
        ["formKey", "editorId", "base", "baseType", kindColumn, "space", "cell", "x", "y", "z"];

    private static ImmutableArray<string> SectorHeader() =>
    [
        .. CompassDirections.All.SelectMany(sector => new[] { $"{sector}Total", $"{sector}Removed", $"{sector}State" }),
        "occupiedDirections", "removedDirections",
        .. CompassDirections.All.Select(sector => $"{sector}Restored"),
        "restoredDirections",
    ];

    /// <param name="evaluations">Listed ordered by record key.</param>
    public static CsvTable BuildLeftBehindTable(
        CollectedObjects world, IEnumerable<LeftBehindCheck> evaluations, MarkerMoves markerMoves, ReportContext context)
    {
        var movesByTarget = PerIndexTable<ReportedMove>.From(ReportedMove.Of(markerMoves), reported => reported.Move.Target.Index);
        return BuildEvaluationTable(
            ReportFileNames.LeftBehindFileName,
            LeftBehindHeader,
            world,
            evaluations.Select(evaluation => (evaluation.TargetIndex, Row: LeftBehindRow(evaluation, world, context.Bases, movesByTarget.TryGet(evaluation.TargetIndex, out var move) ? move : null))));
    }

    /// <param name="evaluations">The target of each evaluation and its row; the rows are ordered by the targets' record keys.</param>
    private static CsvTable BuildEvaluationTable(
        string fileName, ImmutableArray<string> header, CollectedObjects world, IEnumerable<(int TargetIndex, ImmutableArray<string> Row)> evaluations) =>
        new(fileName, header, [.. evaluations.OrderBy(evaluation => world.Targets[evaluation.TargetIndex].Key, RecordKeyTextOrder.Comparer).Select(evaluation => evaluation.Row)]);

    private static ImmutableArray<string> LeftBehindRow(LeftBehindCheck evaluation, CollectedObjects world, IBaseFacts bases, ReportedMove? move)
    {
        var inside = evaluation.ContainingObject;
        return CsvRow.Of(
        [
            .. LeftBehindObjectColumns(world.Targets[evaluation.TargetIndex], evaluation.Kind.ToString(), world, bases),
            Number(evaluation.Radius),
            OptionalText(inside?.Key.ToString()),
            OptionalText(inside?.WinningMod.ToString()),
            .. SectorColumns(evaluation.Surroundings),
            Quote(LeftBehindOutcomeText.Verdict(evaluation)),
            Quote(LeftBehindOutcomeText.DescribeReason(evaluation)),
            .. CsvRow.EmptyOrValues(
                move, MoveColumnCount, reported => [.. Numbers(reported.Move.To), Number(reported.Distance), Quote(reported.Detail.Surface.ToString())]),
        ]);
    }

    /// <summary>The columns of <see cref="LeftBehindObjectHeader"/>.</summary>
    private static IEnumerable<string> LeftBehindObjectColumns(TargetObject target, string kind, CollectedObjects world, IBaseFacts bases) =>
    [
        Quote(target.Key.ToString()),
        OptionalText(target.EditorId),
        Quote(RecordNames.DescribeBase(bases, target.Base)),
        Quote(DescribeBaseType(bases, target.Base)),
        Quote(kind),
        Quote(Describe.Space(world, target.SpaceKey)),
        Quote(Describe.CellOrEmpty(target)),
        .. Numbers(target.Transform.Position),
    ];

    /// <summary>The columns of <see cref="SectorHeader"/>.</summary>
    private static IEnumerable<string> SectorColumns(SectorAreas surroundings) =>
    [
        .. CompassDirections.All.SelectMany(sector => new[]
        {
            Number(surroundings.Total(sector)), Number(surroundings.Removed(sector)), Quote(surroundings.State(sector).ToString()),
        }),
        Int(surroundings.OccupiedCount),
        Int(surroundings.RemovedCount),
        .. CompassDirections.All.Select(sector => Number(surroundings.Restored(sector))),
        Int(surroundings.RestoredCount),
    ];

    private static string DescribeBaseType(IBaseFacts bases, BaseKey? baseKey) =>
        baseKey is { } key && bases.Of(key) is { Resolved: true } facts ? facts.RecordTypeName! : string.Empty;
}
