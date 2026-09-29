using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using static AddedObjectRemover.CsvFormat;

namespace AddedObjectRemover;

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

    private static ImmutableArray<CsvTable> TouchTables(Outcome outcome, TouchExplanation touch, float tolerance, ReportContext context)
    {
        var followUp = outcome.FollowUp;
        var seedRound = followUp.Ledger.Rounds[followUp.Ledger.Rounds.Length - followUp.Rounds.Length - 1];
        var seeds = Decisions.RemovalsIn(followUp.Ledger, outcome.World, LeftoverResult.None, seedRound).ToList();
        var removals = followUp.Rounds.SelectMany(round => Decisions.RemovalsIn(followUp.Ledger, outcome.World, LeftoverResult.None, round)).ToList();
        var kept = followUp.Rounds.SelectMany(round => Decisions.KeptIn(followUp.Ledger, round)).ToList();
        return
        [
            Edges(outcome.World, seeds, touch, tolerance, context),
            Components(outcome.World, seeds, removals, kept, touch, context),
        ];
    }

    /// <param name="seeds">The too-close round's removals.</param>
    public static CsvTable Edges(
        World world, IEnumerable<Removal> seeds, TouchExplanation touch, float tolerance, ReportContext context)
    {
        var seedReasonByTarget = SeedReasons(seeds, world.Targets);
        var rows = touch.Edges
            .Select(edge => new EdgeRow(
                edge.ComponentId,
                world.Targets[edge.Pair.First],
                world.Targets[edge.Pair.Second],
                seedReasonByTarget.ContainsKey(edge.Pair.First),
                seedReasonByTarget.ContainsKey(edge.Pair.Second),
                edge.MinSurfaceDistance))
            .OrderBy(row => row.ComponentId)
            .ThenBy(row => row.First.Key.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row.Second.Key.ToString(), StringComparer.Ordinal)
            .Select(row => EdgeFields(row, context, tolerance));
        return new CsvTable(EdgesFileName, EdgeHeader, [.. rows]);
    }

    /// <param name="seeds">The too-close round's removals.</param>
    /// <param name="removals">The follow-up rounds' removals.</param>
    /// <param name="kept">The objects the follow-up rounds held.</param>
    public static CsvTable Components(
        World world,
        IEnumerable<Removal> seeds,
        IEnumerable<Removal> removals,
        IEnumerable<KeptTarget> kept,
        TouchExplanation touch,
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
                members.Count(seedReasonByTarget.ContainsKey),
                removedByComponent.GetValueOrDefault(componentId),
                keptByComponent.GetValueOrDefault(componentId),
                DescribeSpaces(members, world),
                ComponentWorldAabb(members, world.Targets, context.Shapes),
                DescribeTopBases(members, world, context.Bases),
                DeepestChain(members, components, world.Targets),
                DescribeSeedReasons(members, seedReasonByTarget))));
        return new CsvTable(ComponentsFileName, ComponentHeader, [.. rows]);
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

    private static Dictionary<int, string> SeedReasons(IEnumerable<Removal> seeds, IReadOnlyList<TargetObject> targets) =>
        seeds.ToDictionary(seed => seed.TargetIndex, seed => DescribeSeedReason(seed, targets));

    private static string DescribeSeedReason(Removal seed, IReadOnlyList<TargetObject> targets) => seed switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"{other.FormKey} ({other.WinningMod})",
        LinkedRemoval linked => $"linked to {targets[linked.LinkedToTargetIndex].Key}",
        _ => throw new UnreachableException($"Unexpected seed removal type {seed.GetType().Name}."),
    };

    private static Dictionary<int, int> CountByComponent(IEnumerable<int> targetIndices, ImmutableArray<int> componentIdByTarget)
    {
        var counts = new Dictionary<int, int>();
        foreach (var index in targetIndices)
        {
            var componentId = componentIdByTarget[index];
            counts[componentId] = counts.GetValueOrDefault(componentId) + 1;
        }
        return counts;
    }

    private static Box ComponentWorldAabb(ImmutableArray<int> members, IReadOnlyList<TargetObject> targets, ShapeCatalog shapes) =>
        members
            .Select(member => OrientedBox.FromLocal(shapes.GetLocalBox(targets[member].Base), targets[member].Transform).WorldAabb(0))
            .Aggregate((a, b) => a.Union(b));

    private static string DescribeSpaces(ImmutableArray<int> members, World world)
    {
        var names = members
            .Select(m => world.SpaceNames.GetValueOrDefault(world.Targets[m].SpaceKey, world.Targets[m].SpaceKey.ToString()))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var listed = names.Take(MaxListedSpaces);
        var suffix = names.Count > MaxListedSpaces ? $"; (+{names.Count - MaxListedSpaces} more)" : string.Empty;
        return string.Join("; ", listed) + suffix;
    }

    private static string DescribeTopBases(ImmutableArray<int> members, World world, IBaseFacts bases)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var name = RecordNames.DescribeBase(bases, world.Targets[member].Base);
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        return string.Join(
            "; ",
            counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTopBases)
                .Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    /// <summary>Path from the root seed to the member with the largest depth; the first such member wins a tie.</summary>
    private static (int Length, string FormKeys) DeepestChain(
        ImmutableArray<int> members, TouchComponentSet components, IReadOnlyList<TargetObject> targets)
    {
        var deepest = members[0];
        foreach (var member in members)
        {
            if (components.DepthOf[member] > components.DepthOf[deepest]) deepest = member;
        }

        var chain = new List<int>();
        for (var node = deepest; node >= 0; node = components.ParentOf[node]) chain.Add(node);
        chain.Reverse();
        return (components.DepthOf[deepest], string.Join(" -> ", chain.Select(node => targets[node].Key.ToString())));
    }

    private static string DescribeSeedReasons(ImmutableArray<int> members, IReadOnlyDictionary<int, string> seedReasonByTarget) =>
        string.Join("; ", members.Where(seedReasonByTarget.ContainsKey).Select(member => seedReasonByTarget[member]));

    private static ImmutableArray<string> EdgeFields(EdgeRow row, ReportContext context, float tolerance) => Row(
    [
        Num(row.ComponentId), Text(row.First.Key.ToString()), Text(row.Second.Key.ToString()),
        Bool(row.FirstIsSeed), Bool(row.SecondIsSeed),
        Num(row.MinSurfaceDistance), Num(tolerance), Num(Vector3.Distance(row.First.Transform.Position, row.Second.Transform.Position)),
        .. TargetColumns(row.First, context),
        .. TargetColumns(row.Second, context),
    ]);

    private static ImmutableArray<string> ComponentFields(ComponentRow row) => Row(
    [
        Num(row.ComponentId), Num(row.Size), Num(row.SeedCount), Num(row.RemovedByTouchCount), Num(row.KeptAsReferencedCount),
        Text(row.Spaces),
        Num(row.WorldAabb.Min.X), Num(row.WorldAabb.Min.Y), Num(row.WorldAabb.Min.Z),
        Num(row.WorldAabb.Max.X), Num(row.WorldAabb.Max.Y), Num(row.WorldAabb.Max.Z),
        Text(row.TopBases), Num(row.DeepestChain.Length), Text(row.DeepestChain.FormKeys), Text(row.SeedReasons),
    ]);

    private static IEnumerable<string> TargetColumns(TargetObject target, ReportContext context)
    {
        var rotation = target.Rotation;
        var halfExtents = context.Shapes.GetLocalBox(target.Base).Scaled(target.Transform.Scale).Size * 0.5f;
        return
        [
            Text(target.EditorId ?? string.Empty),
            Text(RecordNames.DescribeBase(context.Bases, target.Base)),
            Text(context.Shapes.GetMeshPath(target.Base) ?? string.Empty),
            Text(target.SpaceKey.ToString()),
            Num(target.Transform.Position.X), Num(target.Transform.Position.Y), Num(target.Transform.Position.Z),
            Num(float.RadiansToDegrees(rotation.X)), Num(float.RadiansToDegrees(rotation.Y)), Num(float.RadiansToDegrees(rotation.Z)),
            Num(target.Transform.Scale),
            Num(halfExtents.X), Num(halfExtents.Y), Num(halfExtents.Z),
        ];
    }
}
