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
        if (restingObjects.Outcome == StepOutcome.Performed && restingObjects.Mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport && restingObjects.HadSeeds)
            tables.Add(BuildSupportTable(outcome.Collected, SupportEvaluationRows.Join(restingObjects, outcome.Final), alsoRemove.SupportLostFraction, context));
        if (ExplanationEvidence.HasMeshOrigins(restingObjects)) tables.Add(BuildMeshOriginsTable(ExplanationEvidence.MeshOrigins(restingObjects)));
        if (outcome.LeftBehind.Outcome != StepOutcome.SwitchedOff) tables.Add(BuildLeftBehindTable(outcome.Collected, outcome.LeftBehind.Evaluations, outcome.MarkerMoves, context));
        tables.Add(BuildHintsTable(outcome.Collected, outcome.Hints));
        return tables.ToImmutable();
    }

    /// <summary>The columns that name a target object's base: its Editor ID, the base and the model path.</summary>
    private static IEnumerable<string> BaseColumns(TargetObject target, ReportContext context) =>
    [
        OptionalText(target.EditorId),
        Quote(RecordNames.DescribeBase(context.Bases, target.Base)),
        OptionalText(context.Shapes.Of(target.Base).MeshPath),
    ];
}
