using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The report files of a run, as tables of fields already formatted as CSV text.</summary>
internal static partial class Tables
{
    /// <param name="explanations">Decides which explanation files exist.</param>
    /// <param name="leftBehind">Null when the left-behind invisible objects step is off.</param>
    /// <returns>In the order the steps ran; none when report files are off.</returns>
    public static ImmutableArray<CsvTable> Build(
        RunOutcome outcome,
        ReportFileDetails explanations,
        ReportOptions reports,
        LeftBehindOptions? leftBehind,
        AlsoRemoveSettings alsoRemove,
        ReportContext context)
    {
        if (!reports.WriteFiles) return [];

        var tables = ImmutableArray.CreateBuilder<CsvTable>();
        if (explanations.Touch is { } touch) tables.AddRange(TouchTables(outcome, touch, alsoRemove.TouchGap, context));
        if (alsoRemove.Mode == FollowUpRemovalMode.ObjectsSupportedByIt && outcome.RestingObjects.HadSeeds)
            tables.Add(Anchoring(outcome.World, AnchoringRows.Join(outcome.RestingObjects), alsoRemove.SupportLostFraction, context));
        if (explanations.MeshOrigins is { } origins) tables.Add(MeshOrigins(origins));
        if (leftBehind != null) tables.Add(LeftBehind(outcome.World, outcome.LeftBehind.Evaluations, outcome.MarkerMoves, context));
        tables.Add(Hints(outcome.World, outcome.Hints));
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
