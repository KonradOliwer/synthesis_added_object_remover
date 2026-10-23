using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private const int MaxListedSupporters = 5;

    private static readonly ImmutableArray<string> SupportHeader =
    [
        "iteration", "formKey", "editorId", "base", "modelPath", "space",
        "contactPoints", "totalWeight",
        "removedCleanedModShare", "keptCleanedModShare", "otherPluginShare", "terrainShare",
        "threshold", "decision", "topSupporters",
    ];

    /// <param name="threshold">The support lost fraction the rule used.</param>
    public static CsvTable BuildSupportTable(CollectedObjects world, IEnumerable<SupportEvaluationRow> evaluations, float threshold, ReportContext context)
    {
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => world.Targets[evaluation.TargetIndex].Key, RecordKeyTextOrder.Comparer)
            .Select(evaluation => SupportRow(evaluation, world, context, threshold));
        return new CsvTable(ReportFileNames.SupportFileName, SupportHeader, [.. rows]);
    }

    private static ImmutableArray<string> SupportRow(SupportEvaluationRow evaluation, CollectedObjects world, ReportContext context, float threshold)
    {
        var target = world.Targets[evaluation.TargetIndex];
        float ShareOf(SupportCategory category) => SupporterShares.TotalOf(evaluation.Shares, category);
        return CsvRow.Of(
        [
            Int(evaluation.Iteration),
            Quote(target.Key.ToString()),
            .. BaseColumns(target, context),
            Quote(Describe.Space(world, target.SpaceKey)),
            Int(evaluation.Contacts.ContactPoints),
            Number(evaluation.Contacts.TotalWeight),
            Number(ShareOf(SupportCategory.RemovedTarget)),
            Number(ShareOf(SupportCategory.KeptTarget)),
            Number(ShareOf(SupportCategory.OtherPlugin)),
            Number(ShareOf(SupportCategory.Terrain)),
            Number(threshold),
            Quote(DescribeDecision(evaluation)),
            Quote(DescribeTopSupporters(evaluation, world)),
        ]);
    }

    private static string DescribeDecision(SupportEvaluationRow evaluation)
    {
        if (evaluation.Removed) return "removed";
        if (evaluation.RemovedAsLinked) return "removed (linked to a removed object)";
        if (evaluation.Held) return "kept (referenced)";
        return evaluation.Contacts.ContactPoints == 0 ? "kept (no contact points)" : "kept";
    }

    private static string DescribeTopSupporters(SupportEvaluationRow evaluation, CollectedObjects world) =>
        TextLists.TopN(
            evaluation.Shares,
            MaxListedSupporters,
            share => $"{DescribeSupporter(share.Supporter, world)} {DescribeCategory(share.Category)} {Number(share.Share)}",
            CellSeparator);

    private static string DescribeCategory(SupportCategory category) => category switch
    {
        SupportCategory.RemovedTarget => "RemovedCleanedMod",
        SupportCategory.KeptTarget => "KeptCleanedMod",
        SupportCategory.OtherPlugin => nameof(SupportCategory.OtherPlugin),
        SupportCategory.Terrain => nameof(SupportCategory.Terrain),
        _ => throw new UnreachableException($"Unknown support category {category}."),
    };

    internal static string DescribeSupporter(Supporter supporter, CollectedObjects world) => supporter.Type switch
    {
        SupporterType.Target => world.Targets[supporter.Index].Key.ToString(),
        SupporterType.PlacedObject => world.Other(new OtherId(supporter.Index)).Key.ToString(),
        SupporterType.Terrain => "terrain",
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };
}
