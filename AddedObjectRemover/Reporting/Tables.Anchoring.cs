using System.Collections.Immutable;
using System.Diagnostics;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover;

internal static partial class Tables
{
    private const int MaxListedSupporters = 5;

    private static readonly ImmutableArray<string> AnchoringHeader =
    [
        "iteration", "formKey", "editorId", "base", "modelPath", "space",
        "contactPoints", "totalWeight",
        "removedTargetShare", "keptTargetShare", "otherPluginShare", "terrainShare",
        "threshold", "decision", "topSupporters",
    ];

    /// <param name="threshold">The support lost fraction the rule used.</param>
    public static CsvTable Anchoring(World world, IEnumerable<AnchoringEvaluation> evaluations, float threshold, ReportContext context)
    {
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => world.Targets[evaluation.TargetIndex].Key.ToString(), StringComparer.Ordinal)
            .Select(evaluation => AnchoringRow(evaluation, world, context, threshold));
        return new CsvTable(AnchoringFileName, AnchoringHeader, [.. rows]);
    }

    private static ImmutableArray<string> AnchoringRow(AnchoringEvaluation evaluation, World world, ReportContext context, float threshold)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return Row(
        [
            Num(evaluation.Iteration),
            Text(target.Key.ToString()),
            Text(target.EditorId ?? string.Empty),
            Text(RecordNames.DescribeBase(context.Bases, target.Base)),
            Text(context.Shapes.GetMeshPath(target.Base) ?? string.Empty),
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
        ]);
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
