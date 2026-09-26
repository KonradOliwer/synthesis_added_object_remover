using System.Numerics;
using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <summary>Result of one diagnostics write, for the console summary line.</summary>
internal readonly record struct TouchDiagnosticsWriteResult(int EdgeCount, int ComponentCount, string EdgesPath, string ComponentsPath);

/// <summary>
/// Optional CSV output that lets a touching chain be judged from the log alone, without opening
/// xEdit: every touching edge among target objects of one explored component (seed-to-seed
/// included), and one row per component with its members, seeds and deepest chain.
/// </summary>
internal static class TouchDiagnosticsWriter
{
    private const int MaxListedSpaces = 10;
    private const int MaxTopBases = 5;

    private static readonly string[] EdgeHeader =
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

    private static readonly string[] ComponentHeader =
    [
        "componentId", "size", "seedCount", "removedByTouchCount", "keptAsReferencedCount",
        "spaces", "worldAabbMinX", "worldAabbMinY", "worldAabbMinZ", "worldAabbMaxX", "worldAabbMaxY", "worldAabbMaxZ",
        "topBaseEditorIds", "deepestChainLength", "deepestChainFormKeys", "seedReasons",
    ];

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
        int DeepestChainLength,
        string DeepestChainFormKeys,
        string SeedReasons);

    /// <param name="folder">Written to as "edges.csv" and "components.csv" inside it.</param>
    public static TouchDiagnosticsWriteResult Write(
        string folder,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        float tolerance,
        IReadOnlyList<TooCloseRemoval> seeds,
        TouchClusters clusters,
        TouchDiagnosticsData diagnostics)
    {
        var seedReasonByTarget = seeds.ToDictionary(s => s.TargetIndex, s => s.TooCloseTo);
        var edgeRows = CreateEdgeRows(scan.Targets, diagnostics, seedReasonByTarget);
        var componentRows = CreateComponentRows(scan, shapes, diagnostics, clusters, seedReasonByTarget);

        var edgesPath = Path.Combine(folder, "edges.csv");
        var componentsPath = Path.Combine(folder, "components.csv");
        CsvFile.Write(edgesPath, EdgeHeader, edgeRows.Select(row => FormatEdge(row, shapes, tolerance)));
        CsvFile.Write(componentsPath, ComponentHeader, componentRows.Select(FormatComponent));
        return new TouchDiagnosticsWriteResult(edgeRows.Count, componentRows.Count, edgesPath, componentsPath);
    }

    private static List<EdgeRow> CreateEdgeRows(
        IReadOnlyList<TargetObject> targets, TouchDiagnosticsData diagnostics, IReadOnlyDictionary<int, OtherObject> seedReasonByTarget) =>
        diagnostics.Edges
            .Select(edge => new EdgeRow(
                edge.ComponentId,
                targets[edge.Pair.First],
                targets[edge.Pair.Second],
                seedReasonByTarget.ContainsKey(edge.Pair.First),
                seedReasonByTarget.ContainsKey(edge.Pair.Second),
                edge.MinSurfaceDistance))
            .OrderBy(row => row.ComponentId)
            .ThenBy(row => row.First.Record.FormKey.ToString(), StringComparer.Ordinal)
            .ThenBy(row => row.Second.Record.FormKey.ToString(), StringComparer.Ordinal)
            .ToList();

    private static List<ComponentRow> CreateComponentRows(
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        TouchDiagnosticsData diagnostics,
        TouchClusters clusters,
        IReadOnlyDictionary<int, OtherObject> seedReasonByTarget)
    {
        var removedByComponent = CountByComponent(clusters.Removals.Select(r => r.TargetIndex), diagnostics.ComponentId);
        var keptByComponent = CountByComponent(clusters.Kept.Select(k => k.TargetIndex), diagnostics.ComponentId);
        var rows = new List<ComponentRow>();
        for (var componentId = 0; componentId < diagnostics.ComponentMembers.Count; componentId++)
        {
            var members = diagnostics.ComponentMembers[componentId];
            var (chainLength, chainFormKeys) = DescribeDeepestChain(members, diagnostics, scan.Targets);
            rows.Add(new ComponentRow(
                componentId,
                members.Count,
                members.Count(seedReasonByTarget.ContainsKey),
                removedByComponent.GetValueOrDefault(componentId),
                keptByComponent.GetValueOrDefault(componentId),
                DescribeSpaces(members, scan),
                ComponentWorldAabb(members, scan.Targets, shapes),
                DescribeTopBases(members, scan, shapes),
                chainLength,
                chainFormKeys,
                DescribeSeedReasons(members, seedReasonByTarget)));
        }
        return rows;
    }

    private static Dictionary<int, int> CountByComponent(IEnumerable<int> targetIndices, int[] componentIdByTarget)
    {
        var counts = new Dictionary<int, int>();
        foreach (var index in targetIndices)
        {
            var componentId = componentIdByTarget[index];
            counts[componentId] = counts.GetValueOrDefault(componentId) + 1;
        }
        return counts;
    }

    private static Box ComponentWorldAabb(IReadOnlyList<int> members, IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes) =>
        members
            .Select(member => OrientedBox.FromLocal(shapes.GetLocalBox(targets[member].Base), targets[member].Transform).WorldAabb(0))
            .Aggregate((a, b) => a.Union(b));

    private static string DescribeSpaces(IReadOnlyList<int> members, ScanResult scan)
    {
        var names = members
            .Select(m => scan.SpaceNames.GetValueOrDefault(scan.Targets[m].SpaceKey, scan.Targets[m].SpaceKey.ToString()))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var listed = names.Take(MaxListedSpaces);
        var suffix = names.Count > MaxListedSpaces ? $"; (+{names.Count - MaxListedSpaces} more)" : string.Empty;
        return string.Join("; ", listed) + suffix;
    }

    private static string DescribeTopBases(IReadOnlyList<int> members, ScanResult scan, BaseObjectShapeProvider shapes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var name = RecordNames.DescribeBase(shapes, scan.Targets[member].Base);
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

    /// <summary>Path from the root seed to the member with the largest BFS depth.</summary>
    private static (int Length, string FormKeys) DescribeDeepestChain(
        IReadOnlyList<int> members, TouchDiagnosticsData diagnostics, IReadOnlyList<TargetObject> targets)
    {
        var deepest = members[0];
        foreach (var member in members)
        {
            if (diagnostics.Depth[member] > diagnostics.Depth[deepest]) deepest = member;
        }

        var chain = new List<int>();
        for (var node = deepest; node >= 0; node = diagnostics.ParentOf[node]) chain.Add(node);
        chain.Reverse();
        return (diagnostics.Depth[deepest], string.Join(" -> ", chain.Select(node => targets[node].Record.FormKey.ToString())));
    }

    private static string DescribeSeedReasons(IReadOnlyList<int> members, IReadOnlyDictionary<int, OtherObject> seedReasonByTarget) =>
        string.Join(
            "; ",
            members
                .Where(seedReasonByTarget.ContainsKey)
                .Select(member => seedReasonByTarget[member])
                .Select(other => $"{other.FormKey} ({other.WinningMod})"));

    private static IEnumerable<string> FormatEdge(EdgeRow row, BaseObjectShapeProvider shapes, float tolerance) =>
    [
        Num(row.ComponentId), Text(row.First.Record.FormKey.ToString()), Text(row.Second.Record.FormKey.ToString()),
        Bool(row.FirstIsSeed), Bool(row.SecondIsSeed),
        Num(row.MinSurfaceDistance), Num(tolerance), Num(Vector3.Distance(row.First.Transform.Position, row.Second.Transform.Position)),
        .. FormatTargetColumns(row.First, shapes),
        .. FormatTargetColumns(row.Second, shapes),
    ];

    private static IEnumerable<string> FormatComponent(ComponentRow row) =>
    [
        Num(row.ComponentId), Num(row.Size), Num(row.SeedCount), Num(row.RemovedByTouchCount), Num(row.KeptAsReferencedCount),
        Text(row.Spaces),
        Num(row.WorldAabb.Min.X), Num(row.WorldAabb.Min.Y), Num(row.WorldAabb.Min.Z),
        Num(row.WorldAabb.Max.X), Num(row.WorldAabb.Max.Y), Num(row.WorldAabb.Max.Z),
        Text(row.TopBases), Num(row.DeepestChainLength), Text(row.DeepestChainFormKeys), Text(row.SeedReasons),
    ];

    private static IEnumerable<string> FormatTargetColumns(TargetObject target, BaseObjectShapeProvider shapes)
    {
        var rotation = target.Record.Placement!.Rotation;
        var halfExtents = shapes.GetLocalBox(target.Base).Scaled(target.Transform.Scale).Size * 0.5f;
        return
        [
            Text(target.Record.EditorID ?? string.Empty),
            Text(RecordNames.DescribeBase(shapes, target.Base)),
            Text(shapes.GetMeshPath(target.Base) ?? string.Empty),
            Text(target.SpaceKey.ToString()),
            Num(target.Transform.Position.X), Num(target.Transform.Position.Y), Num(target.Transform.Position.Z),
            Num(float.RadiansToDegrees(rotation.X)), Num(float.RadiansToDegrees(rotation.Y)), Num(float.RadiansToDegrees(rotation.Z)),
            Num(target.Transform.Scale),
            Num(halfExtents.X), Num(halfExtents.Y), Num(halfExtents.Z),
        ];
    }
}
