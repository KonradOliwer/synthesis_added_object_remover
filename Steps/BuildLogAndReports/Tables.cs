using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The report files of a run, as tables of fields already formatted as CSV text.</summary>
internal static partial class Tables
{
    /// <param name="alsoRemove">Only for the printed touch distance and support threshold; whether a table exists comes from the step's result and the status of its report-file details.</param>
    /// <remarks>A stopped left-behind step still gets its header-only table.</remarks>
    /// <returns>In the order the steps ran.</returns>
    public static ImmutableArray<CsvTable> BuildTables(
        RunOutcome outcome,
        AlsoRemoveSettings alsoRemove,
        ReportContext context)
    {
        var tables = ImmutableArray.CreateBuilder<CsvTable>();
        var restingObjects = outcome.RestingObjects;
        if (ExplanationEvidence.HasTouch(restingObjects)) tables.AddRange(BuildTouchTables(outcome, ExplanationEvidence.Touch(restingObjects), alsoRemove.TouchGap, context));
        var afterVisibleLeftBehind = outcome.RestingObjectsAfterVisibleLeftBehind;
        if (HasAnchoring(restingObjects) || HasAnchoring(afterVisibleLeftBehind))
        {
            var evaluations = SupportEvaluationRows.Join(restingObjects, outcome.Final)
                .AddRange(SupportEvaluationRows.Join(afterVisibleLeftBehind, outcome.Final, iterationsBefore: restingObjects.Rounds.Length));
            tables.Add(BuildSupportTable(outcome.Collected, evaluations, alsoRemove.SupportLostFraction, context));
        }
        if (ExplanationEvidence.HasMeshOrigins(restingObjects)) tables.Add(BuildMeshOriginsTable(ExplanationEvidence.MeshOrigins(restingObjects)));
        if (outcome.VisibleLeftBehind.Outcome != StepOutcome.SwitchedOff) tables.Add(BuildVisibleLeftBehindTable(outcome.Collected, outcome.VisibleLeftBehind.Evaluations, context));
        if (outcome.LeftBehind.Outcome != StepOutcome.SwitchedOff) tables.Add(BuildLeftBehindTable(outcome.Collected, outcome.LeftBehind.Evaluations, outcome.MarkerMoves, context));
        tables.Add(BuildRemovalsTable(outcome.Collected, outcome.Removals, outcome.Final, context));
        if (BuildRestoredTable(outcome.Collected, outcome.Restorations, outcome, context) is { } restored) tables.Add(restored);
        if (BuildFitsTable(outcome.Collected, outcome.Final, context) is { } fits) tables.Add(fits);
        if (BuildLeftWithoutSupportTable(outcome.Collected, outcome.OtherModsObjects, context) is { } leftWithoutSupport) tables.Add(leftWithoutSupport);
        tables.Add(BuildHintsTable(outcome.Collected, outcome.Hints));
        return tables.ToImmutable();
    }

    private static bool HasAnchoring(RestingObjectsResult alsoRemove) =>
        alsoRemove.Outcome == StepOutcome.Performed && alsoRemove.Mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport && alsoRemove.HadSeeds;

    /// <summary>The columns that name a target object's record: its form key, its Editor ID and its base.</summary>
    private static IEnumerable<string> RecordColumns(TargetObject target, ReportContext context) =>
    [
        Quote(target.Key.ToString()),
        OptionalText(target.EditorId),
        Quote(RecordNames.DescribeBase(context.Bases, target.Base)),
    ];

    /// <summary>The columns that name a target object's base: its Editor ID, the base and the model path.</summary>
    private static IEnumerable<string> BaseColumns(TargetObject target, ReportContext context) =>
    [
        OptionalText(target.EditorId),
        Quote(RecordNames.DescribeBase(context.Bases, target.Base)),
        OptionalText(context.Shapes.Of(target.Base).MeshPath),
    ];
}
