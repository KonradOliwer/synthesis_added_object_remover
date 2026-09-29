using System.Numerics;
using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <param name="TriangleBounds">Local bounds of the mesh's triangles.</param>
/// <param name="OriginFractions">Where the mesh origin sits inside its triangle bounds per axis: 0 at the minimum, 1 at the maximum; NaN on an axis without size.</param>
internal sealed record MeshOriginRow(
    string ModelPath,
    string BaseEditorIds,
    int ReferenceCount,
    Box TriangleBounds,
    Vector3 OriginFractions,
    string Classification);

internal readonly record struct MeshOriginSummary(int Meshes, int NearBottom, int NearCentre, int Other);

/// <summary>
/// Optional mesh-origins.csv: for every mesh with triangles used by target objects, where its
/// origin sits inside the bounds of its triangles. Anchoring weights contact points by closeness
/// to the origin, which assumes the origin marks where an object rests; this file shows how often
/// that holds.
/// </summary>
internal static class MeshOriginDiagnosticsWriter
{
    public const string FileName = "mesh-origins.csv";

    public const string NearBottom = "origin near bottom";
    public const string NearCentre = "origin near centre";
    public const string Other = "other";

    private const float BottomMaxFraction = 0.1f;
    private const float CentreMinFraction = 0.35f;
    private const float CentreMaxFraction = 0.65f;

    private static readonly string[] Header =
    [
        "modelPath", "baseEditorIds", "referenceCount",
        "localMinX", "localMinY", "localMinZ", "localMaxX", "localMaxY", "localMaxZ",
        "originFractionX", "originFractionY", "originFractionZ", "classification",
    ];

    /// <summary>One row per target mesh with usable triangles, ordered by model path.</summary>
    public static List<MeshOriginRow> CreateRows(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        IBaseFacts bases,
        TriangleStore meshCache,
        ParallelOptions parallelOptions)
    {
        var users = targets
            .Where(target => shapes.GetMeshPath(target.Base) != null)
            .GroupBy(target => shapes.GetMeshPath(target.Base)!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bounds = MeasureTriangleBounds(users.Select(group => group.Key).ToList(), meshCache, parallelOptions);

        var rows = new List<MeshOriginRow>();
        for (var i = 0; i < users.Count; i++)
        {
            if (bounds[i] is { } triangleBounds) rows.Add(CreateRow(users[i].Key, users[i].ToList(), triangleBounds, bases));
        }
        return rows;
    }

    public static MeshOriginSummary Summarize(IReadOnlyList<MeshOriginRow> rows) => new(
        rows.Count,
        rows.Count(row => row.Classification == NearBottom),
        rows.Count(row => row.Classification == NearCentre),
        rows.Count(row => row.Classification == Other));

    /// <returns>The path written to.</returns>
    public static string Write(string folder, IEnumerable<MeshOriginRow> rows)
    {
        var path = Path.Combine(folder, FileName);
        CsvFile.Write(path, Header, rows.Select(FormatRow));
        return path;
    }

    /// <returns>Per mesh: the bounds of its triangles, or null when it has no usable triangles.</returns>
    private static Box?[] MeasureTriangleBounds(IReadOnlyList<string> meshPaths, TriangleStore cache, ParallelOptions parallelOptions)
    {
        var bounds = new Box?[meshPaths.Count];
        Parallel.For(0, meshPaths.Count, parallelOptions, i =>
        {
            using var lease = cache.Acquire(meshPaths[i]);
            bounds[i] = lease.Tree?.Bounds;
        });
        return bounds;
    }

    private static MeshOriginRow CreateRow(string modelPath, IReadOnlyList<TargetObject> users, Box triangleBounds, IBaseFacts bases)
    {
        var fractions = OriginFractions(triangleBounds);
        return new MeshOriginRow(modelPath, DescribeBases(users, bases), users.Count, triangleBounds, fractions, Classify(fractions.Z));
    }

    private static string DescribeBases(IEnumerable<TargetObject> users, IBaseFacts bases) =>
        string.Join(
            "; ",
            users
                .Select(user => user.Base)
                .OfType<BaseRef>()
                .DistinctBy(reference => reference.FormKey)
                .Select(reference => DescribeBaseEditorId(bases.Of(reference), reference))
                .Order(StringComparer.OrdinalIgnoreCase));

    private static string DescribeBaseEditorId(BaseFacts facts, BaseRef reference) =>
        (facts.Resolved ? facts.EditorId : null) ?? reference.FormKey.ToString();

    private static Vector3 OriginFractions(Box bounds) => new(
        OriginFraction(bounds.Min.X, bounds.Max.X),
        OriginFraction(bounds.Min.Y, bounds.Max.Y),
        OriginFraction(bounds.Min.Z, bounds.Max.Z));

    private static float OriginFraction(float min, float max) => max > min ? -min / (max - min) : float.NaN;

    private static string Classify(float zFraction)
    {
        if (zFraction <= BottomMaxFraction) return NearBottom;
        return zFraction is >= CentreMinFraction and <= CentreMaxFraction ? NearCentre : Other;
    }

    private static IEnumerable<string> FormatRow(MeshOriginRow row) =>
    [
        Text(row.ModelPath), Text(row.BaseEditorIds), Num(row.ReferenceCount),
        Num(row.TriangleBounds.Min.X), Num(row.TriangleBounds.Min.Y), Num(row.TriangleBounds.Min.Z),
        Num(row.TriangleBounds.Max.X), Num(row.TriangleBounds.Max.Y), Num(row.TriangleBounds.Max.Z),
        Num(row.OriginFractions.X), Num(row.OriginFractions.Y), Num(row.OriginFractions.Z),
        Text(row.Classification),
    ];
}
