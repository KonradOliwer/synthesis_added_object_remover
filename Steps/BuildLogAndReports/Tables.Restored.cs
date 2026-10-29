using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private const string OldPlaceNotChecked = "not checked";
    private const string OldPlaceFree = "free";

    /// <remarks>A property, not a field: the field initializers of the parts of this class run in no defined order, and this one reads <see cref="RemovalsHeader"/>.</remarks>
    private static ImmutableArray<string> RestoredHeader =>
    [
        .. RemovalsHeader,
        "earlierPlugin", "newX", "newY", "newZ", "newRotZDeg", "newScale", "newBase", "oldPlaceCheck",
    ];

    /// <summary>One row per restored object, in the order of the removals table: where it stood, why it was taken from there, and the version it went back to.</summary>
    /// <remarks>The new base is empty when the earlier version has the same base object.</remarks>
    /// <returns>Null when nothing was restored.</returns>
    public static CsvTable? BuildRestoredTable(CollectedObjects world, IEnumerable<ObjectWithReason> restorations, RunOutcome outcome, ReportContext context)
    {
        ImmutableArray<ImmutableArray<string>> rows = [.. InAreaOrder(world, restorations).Select(restoration => RestoredRow(world, restoration, outcome, context))];
        return rows.IsEmpty ? null : new CsvTable(ReportFileNames.RestoredFileName, RestoredHeader, rows);
    }

    private static ImmutableArray<string> RestoredRow(CollectedObjects world, ObjectWithReason restoration, RunOutcome outcome, ReportContext context)
    {
        var restored = (Decision.Restored)outcome.Final.Of(new TargetId(restoration.TargetIndex))!;
        var earlier = restored.EarlierVersion;
        return CsvRow.Of(
        [
            .. RemovedObjectColumns(world, restoration, outcome.Final, context),
            Quote(earlier.Plugin.ToString()),
            .. PlacementColumns(earlier.Placement),
            NewBaseColumn(earlier, context),
            Quote(outcome.ChangedObjects.OldPlaceChecks.Any(check => check.Target.Index == restoration.TargetIndex) ? OldPlaceFree : OldPlaceNotChecked),
        ]);
    }
}
