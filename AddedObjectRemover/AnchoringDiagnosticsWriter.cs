using Mutagen.Bethesda.Plugins;
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
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<AnchoringEvaluation> evaluations,
        float threshold)
    {
        var path = Path.Combine(folder, FileName);
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => scan.Targets[evaluation.TargetIndex].Record.FormKey.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, scan, shapes, indexes, threshold));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(
        AnchoringEvaluation evaluation,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
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
            Num(ShareOf(evaluation, SupportCategory.KeptTarget)),
            Num(ShareOf(evaluation, SupportCategory.OtherPlugin)),
            Num(ShareOf(evaluation, SupportCategory.Terrain)),
            Num(threshold),
            Text(DescribeDecision(evaluation)),
            Text(DescribeTopSupporters(evaluation, scan, indexes[target.SpaceKey])),
        ];
    }

    private static float ShareOf(AnchoringEvaluation evaluation, SupportCategory category) =>
        evaluation.Shares.Where(share => share.Category == category).Sum(share => share.Share);

    private static string DescribeDecision(AnchoringEvaluation evaluation)
    {
        if (evaluation.Removed) return "removed";
        return evaluation.Contacts.ContactPoints == 0 ? "kept (no contact points)" : "kept";
    }

    private static string DescribeTopSupporters(AnchoringEvaluation evaluation, ScanResult scan, OtherObjectIndex others) =>
        string.Join(
            "; ",
            evaluation.Shares
                .Take(MaxListedSupporters)
                .Select(share => $"{DescribeSupporter(share.Supporter, scan, others)} {share.Category} {Num(share.Share)}"));

    private static string DescribeSupporter(Supporter supporter, ScanResult scan, OtherObjectIndex others) => supporter.Type switch
    {
        SupporterType.Target => scan.Targets[supporter.Index].Record.FormKey.ToString(),
        SupporterType.OtherObject => others[supporter.Index].FormKey.ToString(),
        _ => "terrain",
    };
}
