using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.CsvFormat;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class Tables
{
    private const int MaxListedSpaces = 10;
    private const int MaxTopBases = 5;

    private static readonly ImmutableArray<string> EdgeHeader =
    [
        "componentId", "firstFormKey", "secondFormKey", "firstIsSeed", "secondIsSeed",
        "measuredMinSurfaceDistance", "tolerance", "centerToCenterDistance",
        "first_editorId", "first_base", "first_modelPath", "first_spaceFormKey",
        "first_posX", "first_posY", "first_posZ", "first_rotXDeg", "first_rotYDeg", "first_rotZDeg",
        "first_scale", "first_halfExtentX", "first_halfExtentY", "first_halfExtentZ",
        "second_editorId", "second_base", "second_modelPath", "second_spaceFormKey",
        "second_posX", "second_posY", "second_posZ", "second_rotXDeg", "second_rotYDeg", "second_rotZDeg",
        "second_scale", "second_halfExtentX", "second_halfExtentY", "second_halfExtentZ",
    ];

    private static readonly ImmutableArray<string> ComponentHeader =
    [
        "componentId", "size", "seedCount", "removedByTouchCount", "keptAsReferencedCount",
        "spaces", "worldAabbMinX", "worldAabbMinY", "worldAabbMinZ", "worldAabbMaxX", "worldAabbMaxY", "worldAabbMaxZ",
        "topBaseEditorIds", "deepestChainLength", "deepestChainFormKeys", "seedReasons",
    ];

    private static ImmutableArray<CsvTable> TouchTables(RunOutcome outcome, TouchChainEdges touch, float tolerance, ReportContext context)
    {
        var alsoRemove = outcome.RestingObjects;
        var seedRound = alsoRemove.RemovalDecisions.Rounds[alsoRemove.RemovalDecisions.Rounds.Length - alsoRemove.Rounds.Length - 1];
        var seeds = RemovalList.RemovalsIn(alsoRemove.RemovalDecisions, outcome.World, LeftBehindResult.None, seedRound).ToList();
        var removals = alsoRemove.Rounds.SelectMany(round => RemovalList.RemovalsIn(alsoRemove.RemovalDecisions, outcome.World, LeftBehindResult.None, round)).ToList();
        var kept = alsoRemove.Rounds.SelectMany(round => RemovalList.KeptIn(alsoRemove.RemovalDecisions, round)).ToList();
        return
        [
            Edges(outcome.World, seeds, touch, tolerance, context),
            Components(outcome.World, seeds, removals, kept, touch, context),
        ];
    }

    /// <param name="seeds">The too-close round's removals.</param>
    public static CsvTable Edges(
        CollectedObjects world, IEnumerable<RemovedObject> seeds, TouchChainEdges touch, float tolerance, ReportContext context)
    {
        var seedReasonByTarget = SeedReasons(seeds, world.Targets);
        var rows = touch.Edges
            .Select(edge => new EdgeRow(
                edge.ComponentId,
                world.Targets[edge.Pair.First],
                world.Targets[edge.Pair.Second],
                seedReasonByTarget.Has(edge.Pair.First),
                seedReasonByTarget.Has(edge.Pair.Second),
                edge.MinSurfaceDistance))
            .OrderBy(row => row.ComponentId)
            .ThenBy(row => row.First.Key, RecordKeyTextOrder.Comparer)
            .ThenBy(row => row.Second.Key, RecordKeyTextOrder.Comparer)
            .Select(row => EdgeFields(row, context, tolerance));
        return new CsvTable(ReportFileNames.EdgesFileName, EdgeHeader, [.. rows]);
    }

    /// <param name="seeds">The too-close round's removals.</param>
    /// <param name="removals">The follow-up rounds' removals.</param>
    /// <param name="kept">The objects the follow-up rounds held.</param>
    public static CsvTable Components(
        CollectedObjects world,
        IEnumerable<RemovedObject> seeds,
        IEnumerable<RemovedObject> removals,
        IEnumerable<KeptObject> kept,
        TouchChainEdges touch,
        ReportContext context)
    {
        var components = touch.Components;
        var seedReasonByTarget = SeedReasons(seeds, world.Targets);
        var removedByComponent = CountByComponent(removals.Select(removal => removal.TargetIndex), components.ComponentOf);
        var keptByComponent = CountByComponent(kept.Select(entry => entry.TargetIndex), components.ComponentOf);
        var rows = components.Members.Select((members, componentId) => ComponentFields(
            new ComponentRow(
                componentId,
                members.Length,
                members.Count(seedReasonByTarget.Has),
                removedByComponent.GetValueOrDefault(componentId),
                keptByComponent.GetValueOrDefault(componentId),
                DescribeSpaces(members, world),
                ComponentWorldAabb(members, world.Targets, context.Shapes),
                DescribeTopBases(members, world, context.Bases),
                DeepestChain(members, components, world.Targets),
                DescribeSeedReasons(members, seedReasonByTarget))));
        return new CsvTable(ReportFileNames.ComponentsFileName, ComponentHeader, [.. rows]);
    }

    private sealed record EdgeRow(
        int ComponentId,
        TargetObject First,
        TargetObject Second,
        bool FirstIsSeed,
        bool SecondIsSeed,
        float MinSurfaceDistance);

    private sealed record ComponentRow(
        int ComponentId,
        int Size,
        int SeedCount,
        int RemovedByTouchCount,
        int KeptAsReferencedCount,
        string Spaces,
        Box WorldAabb,
        string TopBases,
        (int Length, string FormKeys) DeepestChain,
        string SeedReasons);

    private static PerIndexTable<string> SeedReasons(IEnumerable<RemovedObject> seeds, IReadOnlyList<TargetObject> targets) =>
        PerIndexTable<string>.From(seeds, seed => seed.TargetIndex, seed => DescribeSeedReason(seed, targets));

    private static string DescribeSeedReason(RemovedObject seed, IReadOnlyList<TargetObject> targets) => seed switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"{other.Key} ({other.WinningMod})",
        LinkedRemoval linked => $"linked to {targets[linked.LinkedToTargetIndex].Key}",
        _ => throw new UnreachableException($"Unexpected seed removal type {seed.GetType().Name}."),
    };

    private static Dictionary<int, int> CountByComponent(IEnumerable<int> targetIndices, ImmutableArray<int> componentIdByTarget) =>
        KeyedGroups.CountBy(targetIndices, index => componentIdByTarget[index], EqualityComparer<int>.Default).ToDictionary();

    private static Box ComponentWorldAabb(ImmutableArray<int> members, IReadOnlyList<TargetObject> targets, IBaseObjectShapes shapes) =>
        Box.UnionAll(members.Select(member =>
            OrientedBox.FromLocal(shapes.Of(targets[member].Base).Box, targets[member].Transform).WorldAabb(0)));

    private static string DescribeSpaces(ImmutableArray<int> members, CollectedObjects world)
    {
        var names = members
            .Select(m => world.Spaces.TryGetValue(world.Targets[m].SpaceKey, out var space)
                ? Describe.Space(space)
                : world.Targets[m].SpaceKey.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return TextLists.Capped(names, MaxListedSpaces, CellSeparator, hidden => $"(+{hidden} more)");
    }

    private static string DescribeTopBases(ImmutableArray<int> members, CollectedObjects world, IBaseFacts bases)
    {
        var counts = KeyedGroups.CountBy(
            members, member => RecordNames.DescribeBase(bases, world.Targets[member].Base), StringComparer.Ordinal);
        return TextLists.TopN(
            KeyedGroups.Rank(counts, StringComparer.OrdinalIgnoreCase), MaxTopBases, kv => $"{kv.Key}:{kv.Value}", CellSeparator);
    }

    /// <summary>Path from the root seed to the member with the largest depth; the first such member wins a tie.</summary>
    private static (int Length, string FormKeys) DeepestChain(
        ImmutableArray<int> members, TouchChainSet components, IReadOnlyList<TargetObject> targets)
    {
        var deepest = KeyedGroups.ArgMaxFirstWins(members, member => components.DepthOf[member], Comparer<int>.Default);

        var chain = ParentChains.ToRoot(deepest, components.ParentOf);
        return (components.DepthOf[deepest], string.Join(" -> ", chain.Select(node => targets[node].Key.ToString())));
    }

    private static string DescribeSeedReasons(ImmutableArray<int> members, PerIndexTable<string> seedReasonByTarget) =>
        string.Join(CellSeparator, members.Where(seedReasonByTarget.Has).Select(seedReasonByTarget.Get));

    private static ImmutableArray<string> EdgeFields(EdgeRow row, ReportContext context, float tolerance) => CsvRow.Of(
    [
        Int(row.ComponentId), Quote(row.First.Key.ToString()), Quote(row.Second.Key.ToString()),
        Bool(row.FirstIsSeed), Bool(row.SecondIsSeed),
        Number(row.MinSurfaceDistance), Number(tolerance), Number(Vector3.Distance(row.First.Transform.Position, row.Second.Transform.Position)),
        .. TargetColumns(row.First, context),
        .. TargetColumns(row.Second, context),
    ]);

    private static ImmutableArray<string> ComponentFields(ComponentRow row) => CsvRow.Of(
    [
        Int(row.ComponentId), Int(row.Size), Int(row.SeedCount), Int(row.RemovedByTouchCount), Int(row.KeptAsReferencedCount),
        Quote(row.Spaces),
        .. Numbers(row.WorldAabb.Min), .. Numbers(row.WorldAabb.Max),
        Quote(row.TopBases), Int(row.DeepestChain.Length), Quote(row.DeepestChain.FormKeys), Quote(row.SeedReasons),
    ]);

    private static IEnumerable<string> TargetColumns(TargetObject target, ReportContext context)
    {
        var rotation = target.Rotation;
        var halfExtents = context.Shapes.Of(target.Base).Box.Scaled(target.Transform.Scale).HalfExtents;
        return
        [
            .. BaseColumns(target, context),
            Quote(target.SpaceKey.ToString()),
            .. Numbers(target.Transform.Position),
            Number(float.RadiansToDegrees(rotation.X)), Number(float.RadiansToDegrees(rotation.Y)), Number(float.RadiansToDegrees(rotation.Z)),
            Number(target.Transform.Scale),
            .. Numbers(halfExtents),
        ];
    }
}
