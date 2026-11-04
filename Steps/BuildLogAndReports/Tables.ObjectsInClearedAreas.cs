using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    /// <remarks>Built on each ask: the area columns are a field of another part of this class, which may be set up after a field here.</remarks>
    private static ImmutableArray<string> ObjectsInClearedAreasHeader =>
    [
        "formKey", "editorId", "base", "space", "cellFormKey", "cellEditorId", "x", "y", "z", "kind", "objectsAround",
        .. AreaHeader,
        "result",
    ];

    /// <param name="evaluations">In target order, as the rows are.</param>
    public static CsvTable BuildObjectsInClearedAreasTable(CollectedObjects world, IEnumerable<ClearedAreaCheck> evaluations, ReportContext context) =>
        new(ReportFileNames.ObjectsInClearedAreasFileName, ObjectsInClearedAreasHeader, [.. evaluations.Select(evaluation => ObjectInClearedAreaRow(world, evaluation, context))]);

    private static ImmutableArray<string> ObjectInClearedAreaRow(CollectedObjects world, ClearedAreaCheck evaluation, ReportContext context)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return CsvRow.Of(
        [
            .. RecordColumns(target, context),
            Quote(Describe.Space(world, target.SpaceKey)),
            OptionalText(target.Cell?.Key.ToString()),
            OptionalText(target.Cell?.EditorId),
            .. Numbers(target.Transform.Position),
            Quote(KindName(evaluation.Kind)),
            Int(evaluation.ObjectsAround),
            .. AreaColumns(evaluation.Area),
            Quote($"{LeftBehindOutcomeText.Verdict(evaluation)}, {LeftBehindOutcomeText.DescribeReason(evaluation)}"),
        ]);
    }

    private static string KindName(ClearedAreaObjectKind kind) => kind switch
    {
        ClearedAreaObjectKind.Drawn => "drawn",
        ClearedAreaObjectKind.EffectOnly => "effect-only",
        ClearedAreaObjectKind.Invisible => "invisible",
        _ => throw new UnreachableException($"Unknown cleared-area object kind {kind}."),
    };
}
