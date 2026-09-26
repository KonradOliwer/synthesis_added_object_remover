using System.Diagnostics;
using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>
/// Optional orphans.csv: one row per invisible target object checked by the orphaned invisible
/// objects step, with the visible/removed target objects around it per quadrant and the decision.
/// </summary>
internal static class OrphanDiagnosticsWriter
{
    public const string FileName = "orphans.csv";

    private static readonly string[] Header =
    [
        "formKey", "editorId", "base", "baseType", "space", "cell", "x", "y", "z",
        .. QuadrantCounts.All.SelectMany(quadrant => new[] { $"{quadrant}Visible", $"{quadrant}Removed" }),
        "visible", "removed", "removedShare", "threshold", "decision", "reason",
    ];

    /// <returns>The path written to.</returns>
    public static string Write(
        string folder,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        IReadOnlyList<OrphanEvaluation> evaluations,
        float threshold)
    {
        var path = Path.Combine(folder, FileName);
        var rows = evaluations
            .OrderBy(evaluation => scan.Targets[evaluation.TargetIndex].Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, scan, shapes, threshold));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(OrphanEvaluation evaluation, ScanResult scan, BaseObjectShapeProvider shapes, float threshold)
    {
        var target = scan.Targets[evaluation.TargetIndex];
        var neighbours = evaluation.Neighbours;
        var position = target.Transform.Position;
        return
        [
            Text(target.Record.FormKey.ToString()),
            Text(target.Record.EditorID ?? string.Empty),
            Text(RecordNames.DescribeBase(shapes, target.Base)),
            Text(DescribeBaseType(shapes, target.Base)),
            Text(scan.SpaceNames[target.SpaceKey]),
            Text(DescribeCell(scan, evaluation.TargetIndex)),
            Num(position.X), Num(position.Y), Num(position.Z),
            .. QuadrantCounts.All.SelectMany(quadrant => new[] { Num(neighbours.Visible(quadrant)), Num(neighbours.Removed(quadrant)) }),
            Num(neighbours.TotalVisible),
            Num(neighbours.TotalRemoved),
            Num(neighbours.RemovedShare),
            Num(threshold),
            Text(evaluation.Decision == OrphanDecision.Removed ? "removed" : "kept"),
            Text(DescribeReason(evaluation)),
        ];
    }

    private static string DescribeBaseType(BaseObjectShapeProvider shapes, BaseRef? baseRef) =>
        baseRef is { } reference && shapes.ResolveBaseOrNull(reference) is { } record ? record.Registration.Name : string.Empty;

    /// <summary>Empty for an interior, whose cell is the space itself.</summary>
    private static string DescribeCell(ScanResult scan, int targetIndex)
    {
        var cell = scan.TargetLocations[targetIndex].WinningCell.Record;
        return cell.FormKey == scan.Targets[targetIndex].SpaceKey ? string.Empty : RecordNames.Describe(cell);
    }

    private static string DescribeReason(OrphanEvaluation evaluation) => evaluation.Decision switch
    {
        OrphanDecision.Removed => "scenery around it removed",
        OrphanDecision.KeptNoSceneryNearby => "no scenery nearby",
        OrphanDecision.KeptNotAllSidesCleared => "not all sides cleared",
        OrphanDecision.KeptShareBelowThreshold => "removed share below threshold",
        OrphanDecision.KeptReferenced => $"referenced: {evaluation.KeepReason}",
        _ => throw new UnreachableException($"Unknown orphan decision {evaluation.Decision}."),
    };
}
