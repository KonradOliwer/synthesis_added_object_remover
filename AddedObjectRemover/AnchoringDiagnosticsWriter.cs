using System.Diagnostics;
using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>
/// Optional anchoring.csv: one row per candidate evaluation of the Anchoring follow-up removal,
/// with its support split by supporter category, its largest supporters and the decision.
/// </summary>
internal static class AnchoringDiagnosticsWriter
{
    public const string FileName = "anchoring.csv";

    private const int MaxListedSupporters = 5;

    private static readonly string[] Header =
    [
        "iteration", "formKey", "editorId", "base", "modelPath", "space",
        "contactPoints", "totalWeight",
        "removedTargetShare", "keptTargetShare", "otherPluginShare", "terrainShare",
        "threshold", "decision", "topSupporters",
    ];

    /// <returns>The path written to.</returns>
    public static string Write(
        string folder,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        SupporterIndex supporters,
        IReadOnlyList<AnchoringEvaluation> evaluations,
        float threshold)
    {
        var path = Path.Combine(folder, FileName);
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => scan.Targets[evaluation.TargetIndex].Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, scan, shapes, supporters, threshold));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(
        AnchoringEvaluation evaluation,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        SupporterIndex supporters,
        float threshold)
    {
        var target = scan.Targets[evaluation.TargetIndex];
        return
        [
            Num(evaluation.Iteration),
            Text(target.Record.FormKey.ToString()),
            Text(target.Record.EditorID ?? string.Empty),
            Text(RecordNames.DescribeBase(shapes, target.Base)),
            Text(shapes.GetMeshPath(target.Base) ?? string.Empty),
            Text(scan.SpaceNames[target.SpaceKey]),
            Num(evaluation.Contacts.ContactPoints),
            Num(evaluation.Contacts.TotalWeight),
            Num(evaluation.RemovedShare),
            Num(evaluation.ShareOf(SupportCategory.KeptTarget)),
            Num(evaluation.ShareOf(SupportCategory.OtherPlugin)),
            Num(evaluation.ShareOf(SupportCategory.Terrain)),
            Num(threshold),
            Text(DescribeDecision(evaluation)),
            Text(DescribeTopSupporters(evaluation, scan, supporters.GetSpace(target.SpaceKey))),
        ];
    }

    private static string DescribeDecision(AnchoringEvaluation evaluation)
    {
        if (evaluation.Removed) return "removed";
        if (evaluation.RemovedAsLinked) return "removed (linked to a removed object)";
        return evaluation.Contacts.ContactPoints == 0 ? "kept (no contact points)" : "kept";
    }

    private static string DescribeTopSupporters(AnchoringEvaluation evaluation, ScanResult scan, OtherObjectIndex placed) =>
        string.Join(
            "; ",
            evaluation.Shares
                .Take(MaxListedSupporters)
                .Select(share => $"{DescribeSupporter(share.Supporter, scan, placed)} {share.Category} {Num(share.Share)}"));

    private static string DescribeSupporter(Supporter supporter, ScanResult scan, OtherObjectIndex placed) => supporter.Type switch
    {
        SupporterType.Target => scan.Targets[supporter.Index].Record.FormKey.ToString(),
        SupporterType.PlacedObject => placed[supporter.Index].FormKey.ToString(),
        SupporterType.Terrain => "terrain",
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };
}
