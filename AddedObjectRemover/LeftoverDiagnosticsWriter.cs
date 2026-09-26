using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>
/// Optional leftover-invisible-objects.csv: one row per invisible target object checked by the
/// leftover invisible objects step, with the visible/removed target objects around it per quadrant
/// and the decision.
/// </summary>
internal static class LeftoverDiagnosticsWriter
{
    public const string FileName = "leftover-invisible-objects.csv";

    private static readonly string[] Header =
    [
        "formKey", "editorId", "base", "baseType", "invisibleType", "space", "cell", "x", "y", "z",
        .. QuadrantCounts.All.SelectMany(quadrant => new[] { $"{quadrant}Visible", $"{quadrant}Removed" }),
        "visible", "removed", "removedShare", "threshold", "decision", "reason",
    ];

    /// <returns>The path written to.</returns>
    public static string Write(
        string folder,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        IReadOnlyList<LeftoverEvaluation> evaluations,
        float threshold)
    {
        var path = Path.Combine(folder, FileName);
        var rows = evaluations
            .OrderBy(evaluation => scan.Targets[evaluation.TargetIndex].Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, scan, shapes, threshold));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(LeftoverEvaluation evaluation, ScanResult scan, BaseObjectShapeProvider shapes, float threshold)
    {
        var target = scan.Targets[evaluation.TargetIndex];
        var surroundings = evaluation.Surroundings;
        var position = target.Transform.Position;
        return
        [
            Text(target.Record.FormKey.ToString()),
            Text(target.Record.EditorID ?? string.Empty),
            Text(RecordNames.DescribeBase(shapes, target.Base)),
            Text(DescribeBaseType(shapes, target.Base)),
            Text(evaluation.Kind.ToString()),
            Text(scan.SpaceNames[target.SpaceKey]),
            Text(target.CellName ?? string.Empty),
            Num(position.X), Num(position.Y), Num(position.Z),
            .. QuadrantCounts.All.SelectMany(quadrant => new[] { Num(surroundings.Visible(quadrant)), Num(surroundings.Removed(quadrant)) }),
            Num(surroundings.TotalVisible),
            Num(surroundings.TotalRemoved),
            Num(surroundings.RemovedShare),
            Num(threshold),
            Text(evaluation.Decision == LeftoverDecision.Removed ? "removed" : "kept"),
            Text(DescribeReason(evaluation)),
        ];
    }

    private static string DescribeBaseType(BaseObjectShapeProvider shapes, BaseRef? baseRef) =>
        baseRef is { } reference && shapes.ResolveBaseOrNull(reference) is { } record ? record.Registration.Name : string.Empty;

    private static string DescribeReason(LeftoverEvaluation evaluation)
    {
        var decision = LeftoverDecisionText.Describe(evaluation.Decision);
        return evaluation.KeepReason is { } keepReason ? $"{decision}: {keepReason.Detail}" : decision;
    }
}
