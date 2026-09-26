using System.Globalization;
using System.Numerics;
using System.Text;

namespace AddedObjectRemover;

/// <summary>Result of one diagnostics write, for the console summary line.</summary>
internal readonly record struct TouchDiagnosticsWriteResult(int EdgeCount, int ComponentCount, string EdgesPath, string ComponentsPath);

/// <summary>
/// Optional CSV output that lets a touching chain be judged from the log alone, without opening
/// xEdit: every touching edge among target objects in the explored components (seed-to-seed
/// included), and one row per component with its members, seeds and longest chain. Runs after all
/// touch computation, single-threaded, and only when <see cref="RunConfig.TouchDiagnosticsFile"/>
/// is set, so it never affects the normal run's results or performance.
/// </summary>
internal static class TouchDiagnosticsWriter
{
    private const int MaxListedSpaces = 10;
    private const int MaxTopBases = 5;
    private const float RadiansToDegreesFactor = 180f / MathF.PI;

    private static readonly string[] EdgeHeader =
    [
        "componentId", "fromFormKey", "toFormKey", "fromIsSeed", "toIsSeed",
        "measuredMinSurfaceDistance", "tolerance", "centerToCenterDistance",
        "from_editorId", "from_base", "from_modelPath", "from_spaceFormKey",
        "from_posX", "from_posY", "from_posZ", "from_rotXDeg", "from_rotYDeg", "from_rotZDeg",
        "from_scale", "from_halfExtentX", "from_halfExtentY", "from_halfExtentZ",
        "to_editorId", "to_base", "to_modelPath", "to_spaceFormKey",
        "to_posX", "to_posY", "to_posZ", "to_rotXDeg", "to_rotYDeg", "to_rotZDeg",
        "to_scale", "to_halfExtentX", "to_halfExtentY", "to_halfExtentZ",
    ];

    private static readonly string[] ComponentHeader =
    [
        "componentId", "size", "seedCount", "removedByTouchCount", "keptAsReferencedCount",
        "spaces", "worldAabbMinX", "worldAabbMinY", "worldAabbMinZ", "worldAabbMaxX", "worldAabbMaxY", "worldAabbMaxZ",
        "topBaseEditorIds", "longestChainLength", "longestChainFormKeys", "seedReasons",
    ];

    /// <param name="filePathBase">Written to as "&lt;filePathBase&gt;.edges.csv" and "&lt;filePathBase&gt;.components.csv".</param>
    public static TouchDiagnosticsWriteResult Write(
        string filePathBase,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        float tolerance,
        IReadOnlyList<TooCloseRemoval> seeds,
        TouchClusters clusters,
        TouchDiagnosticsData diagnostics)
    {
        var seedTargetIndices = seeds.Select(s => s.TargetIndex).ToHashSet();
        var seedReasonByTarget = seeds.ToDictionary(s => s.TargetIndex, s => s.TooCloseTo);

        var edgesPath = filePathBase + ".edges.csv";
        var componentsPath = filePathBase + ".components.csv";
        var edgeCount = WriteEdges(edgesPath, scan, shapes, tolerance, diagnostics, seedTargetIndices);
        var componentCount = WriteComponents(componentsPath, scan, shapes, diagnostics, clusters, seedTargetIndices, seedReasonByTarget);
        return new TouchDiagnosticsWriteResult(edgeCount, componentCount, edgesPath, componentsPath);
    }

    private static int WriteEdges(
        string path,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        float tolerance,
        TouchDiagnosticsData diagnostics,
        HashSet<int> seedTargetIndices)
    {
        var targets = scan.Targets;
        var edges = CollectTouchingEdges(diagnostics);
        edges.Sort((a, b) => CompareEdges(a, b, targets));

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(",", EdgeHeader));

        var meshCache = new Dictionary<string, MeshTriangleTree?>(StringComparer.OrdinalIgnoreCase);
        var scratch = new TouchScratch();
        foreach (var (componentId, from, to) in edges)
        {
            var fromTarget = targets[from];
            var toTarget = targets[to];
            var distance = MeasureDistance(fromTarget, toTarget, tolerance, shapes, meshCache, scratch);
            var centerDistance = Vector3Distance(fromTarget.Transform.Position, toTarget.Transform.Position);
            var fromInfo = Describe(fromTarget, shapes);
            var toInfo = Describe(toTarget, shapes);
            var fields = new List<string>
            {
                Num(componentId), Csv(fromTarget.Record.FormKey.ToString()), Csv(toTarget.Record.FormKey.ToString()),
                Bool(seedTargetIndices.Contains(from)), Bool(seedTargetIndices.Contains(to)),
                Num(distance), Num(tolerance), Num(centerDistance),
            };
            fields.AddRange(fromInfo);
            fields.AddRange(toInfo);
            writer.WriteLine(string.Join(",", fields));
        }
        return edges.Count;
    }

    /// <summary>Every candidate pair the narrow phase found touching, restricted to explored (componentId &gt;= 0) targets.</summary>
    private static List<(int ComponentId, int From, int To)> CollectTouchingEdges(TouchDiagnosticsData diagnostics)
    {
        var edges = new List<(int ComponentId, int From, int To)>();
        for (var k = 0; k < diagnostics.Candidates.Pairs.Count; k++)
        {
            if (diagnostics.PairResults[k] != PairTouch.Touching) continue;
            var pair = diagnostics.Candidates.Pairs[k];
            var componentId = diagnostics.ComponentId[pair.First];
            if (componentId < 0) continue;
            edges.Add((componentId, pair.First, pair.Second));
        }
        return edges;
    }

    private static int CompareEdges(
        (int ComponentId, int From, int To) a, (int ComponentId, int From, int To) b, IReadOnlyList<TargetObject> targets)
    {
        var byComponent = a.ComponentId.CompareTo(b.ComponentId);
        if (byComponent != 0) return byComponent;
        var byFrom = string.CompareOrdinal(targets[a.From].Record.FormKey.ToString(), targets[b.From].Record.FormKey.ToString());
        return byFrom != 0 ? byFrom : string.CompareOrdinal(targets[a.To].Record.FormKey.ToString(), targets[b.To].Record.FormKey.ToString());
    }

    private static int WriteComponents(
        string path,
        ScanResult scan,
        BaseObjectShapeProvider shapes,
        TouchDiagnosticsData diagnostics,
        TouchClusters clusters,
        HashSet<int> seedTargetIndices,
        IReadOnlyDictionary<int, OtherObject> seedReasonByTarget)
    {
        var removedByComponent = CountByComponent(clusters.Removals.Select(r => r.TargetIndex), diagnostics.ComponentId);
        var keptByComponent = CountByComponent(clusters.Kept.Select(k => k.TargetIndex), diagnostics.ComponentId);

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(",", ComponentHeader));

        for (var componentId = 0; componentId < diagnostics.ComponentMembers.Count; componentId++)
        {
            var members = diagnostics.ComponentMembers[componentId];
            var seedCount = members.Count(seedTargetIndices.Contains);
            var (min, max) = ComponentWorldAabb(members, scan.Targets, shapes);
            var fields = new List<string>
            {
                Num(componentId),
                Num(members.Count),
                Num(seedCount),
                Num(removedByComponent.GetValueOrDefault(componentId)),
                Num(keptByComponent.GetValueOrDefault(componentId)),
                Csv(DescribeSpaces(members, scan)),
                Num(min.X), Num(min.Y), Num(min.Z), Num(max.X), Num(max.Y), Num(max.Z),
                Csv(DescribeTopBases(members, scan, shapes)),
            };
            var (chainLength, chainFormKeys) = DescribeLongestChain(members, diagnostics, scan.Targets);
            fields.Add(Num(chainLength));
            fields.Add(Csv(chainFormKeys));
            fields.Add(Csv(DescribeSeedReasons(members, seedTargetIndices, seedReasonByTarget)));
            writer.WriteLine(string.Join(",", fields));
        }
        return diagnostics.ComponentMembers.Count;
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

    private static (Vector3 Min, Vector3 Max) ComponentWorldAabb(
        IReadOnlyList<int> members, IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes)
    {
        var aabb = OrientedBox.FromLocal(shapes.GetLocalBox(targets[members[0]].Base), targets[members[0]].Transform).WorldAabb(0);
        for (var i = 1; i < members.Count; i++)
        {
            var target = targets[members[i]];
            aabb = aabb.Union(OrientedBox.FromLocal(shapes.GetLocalBox(target.Base), target.Transform).WorldAabb(0));
        }
        return (aabb.Min, aabb.Max);
    }

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

    /// <summary>Longest path, by edge count, from any component member back to the component's root seed.</summary>
    private static (int Length, string FormKeys) DescribeLongestChain(
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

    private static string DescribeSeedReasons(
        IReadOnlyList<int> members, HashSet<int> seedTargetIndices, IReadOnlyDictionary<int, OtherObject> seedReasonByTarget) =>
        string.Join(
            "; ",
            members
                .Where(seedTargetIndices.Contains)
                .Select(member => seedReasonByTarget.TryGetValue(member, out var other)
                    ? $"{other.FormKey} ({other.WinningMod})"
                    : "(unknown)"));

    /// <summary>One touching edge's measured minimum surface distance, world units; NaN if either side has no readable mesh.</summary>
    private static float MeasureDistance(
        TargetObject from,
        TargetObject to,
        float tolerance,
        BaseObjectShapeProvider shapes,
        Dictionary<string, MeshTriangleTree?> meshCache,
        TouchScratch scratch)
    {
        var fromTree = GetTree(shapes.GetMeshPath(from.Base), shapes, meshCache);
        var toTree = GetTree(shapes.GetMeshPath(to.Base), shapes, meshCache);
        if (fromTree == null || toTree == null) return float.NaN;
        return MeshTouchTest.MinSurfaceDistance(fromTree, from.Transform, toTree, to.Transform, tolerance, scratch);
    }

    private static MeshTriangleTree? GetTree(string? meshPath, BaseObjectShapeProvider shapes, Dictionary<string, MeshTriangleTree?> cache)
    {
        if (meshPath == null) return null;
        if (cache.TryGetValue(meshPath, out var cached)) return cached;
        var tree = shapes.ReadGeometry(meshPath) is { } geometry ? MeshTriangleTree.Build(geometry) : null;
        cache[meshPath] = tree;
        return tree;
    }

    private static List<string> Describe(TargetObject target, BaseObjectShapeProvider shapes)
    {
        var scaledSize = shapes.GetLocalBox(target.Base).Scaled(target.Transform.Scale).Size;
        return
        [
            Csv(target.Record.EditorID ?? string.Empty),
            Csv(RecordNames.DescribeBase(shapes, target.Base)),
            Csv(shapes.GetMeshPath(target.Base) ?? string.Empty),
            Csv(target.SpaceKey.ToString()),
            Num(target.Transform.Position.X), Num(target.Transform.Position.Y), Num(target.Transform.Position.Z),
            Num(target.RotationRadians.X * RadiansToDegreesFactor),
            Num(target.RotationRadians.Y * RadiansToDegreesFactor),
            Num(target.RotationRadians.Z * RadiansToDegreesFactor),
            Num(target.Transform.Scale),
            Num(scaledSize.X * 0.5f), Num(scaledSize.Y * 0.5f), Num(scaledSize.Z * 0.5f),
        ];
    }

    private static float Vector3Distance(Vector3 a, Vector3 b) => (a - b).Length();

    private static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>CSV-escapes a field: quoted, with embedded quotes doubled, whenever it holds a comma, quote or newline.</summary>
    private static string Csv(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
