using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private static readonly ImmutableArray<string> VisibleLeftBehindHeader =
    [
        .. LeftBehindObjectHeader("visibleType"),
        .. SectorHeader(),
        "decision", "reason",
    ];

    /// <param name="evaluations">Listed ordered by record key.</param>
    public static CsvTable BuildVisibleLeftBehindTable(CollectedObjects world, IEnumerable<VisibleLeftBehindCheck> evaluations, ReportContext context) =>
        BuildEvaluationTable(
            ReportFileNames.VisibleLeftBehindFileName,
            VisibleLeftBehindHeader,
            world,
            evaluations.Select(evaluation => (evaluation.TargetIndex, Row: VisibleLeftBehindRow(evaluation, world, context.Bases))));

    private static ImmutableArray<string> VisibleLeftBehindRow(VisibleLeftBehindCheck evaluation, CollectedObjects world, IBaseFacts bases) =>
        CsvRow.Of(
        [
            .. LeftBehindObjectColumns(world.Targets[evaluation.TargetIndex], VisibleLeftBehindOutcomeText.KindNameOrEmpty(evaluation.Kind), world, bases),
            .. SectorColumns(evaluation.Surroundings),
            Quote(VisibleLeftBehindOutcomeText.Verdict(evaluation)),
            Quote(VisibleLeftBehindOutcomeText.DescribeReason(evaluation)),
        ]);
}
