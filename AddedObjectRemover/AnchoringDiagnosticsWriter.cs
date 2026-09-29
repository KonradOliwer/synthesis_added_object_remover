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
        World world,
        ShapeCatalog shapes,
        IBaseFacts bases,
        IReadOnlyList<AnchoringEvaluation> evaluations,
        float threshold)
    {
        var path = Path.Combine(folder, FileName);
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => world.Targets[evaluation.TargetIndex].Key.ToString(), StringComparer.Ordinal)
            .Select(evaluation => FormatRow(evaluation, world, shapes, bases, threshold));
        CsvFile.Write(path, Header, rows);
        return path;
    }

    private static IEnumerable<string> FormatRow(
        AnchoringEvaluation evaluation,
        World world,
        ShapeCatalog shapes,
        IBaseFacts bases,
        float threshold)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return
        [
            Num(evaluation.Iteration),
            Text(target.Key.ToString()),
            Text(target.EditorId ?? string.Empty),
            Text(RecordNames.DescribeBase(bases, target.Base)),
            Text(shapes.GetMeshPath(target.Base) ?? string.Empty),
            Text(world.SpaceNames[target.SpaceKey]),
            Num(evaluation.Contacts.ContactPoints),
            Num(evaluation.Contacts.TotalWeight),
            Num(evaluation.RemovedShare),
            Num(evaluation.ShareOf(SupportCategory.KeptTarget)),
            Num(evaluation.ShareOf(SupportCategory.OtherPlugin)),
            Num(evaluation.ShareOf(SupportCategory.Terrain)),
            Num(threshold),
            Text(DescribeDecision(evaluation)),
            Text(DescribeTopSupporters(evaluation, world)),
        ];
    }

    private static string DescribeDecision(AnchoringEvaluation evaluation)
    {
        if (evaluation.Removed) return "removed";
        if (evaluation.RemovedAsLinked) return "removed (linked to a removed object)";
        if (evaluation.Held) return "kept (referenced)";
        return evaluation.Contacts.ContactPoints == 0 ? "kept (no contact points)" : "kept";
    }

    private static string DescribeTopSupporters(AnchoringEvaluation evaluation, World world) =>
        string.Join(
            "; ",
            evaluation.Shares
                .Take(MaxListedSupporters)
                .Select(share => $"{DescribeSupporter(share.Supporter, world)} {share.Category} {Num(share.Share)}"));

    private static string DescribeSupporter(Supporter supporter, World world) => supporter.Type switch
    {
        SupporterType.Target => world.Targets[supporter.Index].Key.ToString(),
        SupporterType.PlacedObject => world.Other(new OtherId(supporter.Index)).FormKey.ToString(),
        SupporterType.Terrain => "terrain",
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };
}
