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

    private static readonly ImmutableArray<string> AnchoringHeader =
    [
        "iteration", "formKey", "editorId", "base", "modelPath", "space",
        "contactPoints", "totalWeight",
        "removedTargetShare", "keptTargetShare", "otherPluginShare", "terrainShare",
        "threshold", "decision", "topSupporters",
    ];

    /// <param name="threshold">The support lost fraction the rule used.</param>
    public static CsvTable Anchoring(CollectedObjects world, IEnumerable<AnchoringEvaluation> evaluations, float threshold, ReportContext context)
    {
        var rows = evaluations
            .OrderBy(evaluation => evaluation.Iteration)
            .ThenBy(evaluation => world.Targets[evaluation.TargetIndex].Key, RecordKeyTextOrder.Comparer)
            .Select(evaluation => AnchoringRow(evaluation, world, context, threshold));
        return new CsvTable(ReportFileNames.AnchoringFileName, AnchoringHeader, [.. rows]);
    }

    private static ImmutableArray<string> AnchoringRow(AnchoringEvaluation evaluation, CollectedObjects world, ReportContext context, float threshold)
    {
        var target = world.Targets[evaluation.TargetIndex];
        return CsvRow.Of(
        [
            Int(evaluation.Iteration),
            Quote(target.Key.ToString()),
            .. BaseColumns(target, context),
            Quote(Describe.Space(world, target.SpaceKey)),
            Int(evaluation.Contacts.ContactPoints),
            Number(evaluation.Contacts.TotalWeight),
            Number(evaluation.RemovedShare),
            Number(evaluation.ShareOf(SupportCategory.KeptTarget)),
            Number(evaluation.ShareOf(SupportCategory.OtherPlugin)),
            Number(evaluation.ShareOf(SupportCategory.Terrain)),
            Number(threshold),
            Quote(DescribeDecision(evaluation)),
            Quote(DescribeTopSupporters(evaluation, world)),
        ]);
    }

    private static string DescribeDecision(AnchoringEvaluation evaluation)
    {
        if (evaluation.Removed) return "removed";
        if (evaluation.RemovedAsLinked) return "removed (linked to a removed object)";
        if (evaluation.Held) return "kept (referenced)";
        return evaluation.Contacts.ContactPoints == 0 ? "kept (no contact points)" : "kept";
    }

    private static string DescribeTopSupporters(AnchoringEvaluation evaluation, CollectedObjects world) =>
        TextLists.TopN(
            evaluation.Shares,
            MaxListedSupporters,
            share => $"{DescribeSupporter(share.Supporter, world)} {share.Category} {Number(share.Share)}",
            CellSeparator);

    private static string DescribeSupporter(Supporter supporter, CollectedObjects world) => supporter.Type switch
    {
        SupporterType.Target => world.Targets[supporter.Index].Key.ToString(),
        SupporterType.PlacedObject => world.Other(new OtherId(supporter.Index)).Key.ToString(),
        SupporterType.Terrain => "terrain",
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };
}
