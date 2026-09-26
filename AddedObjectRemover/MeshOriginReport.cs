using System.Numerics;
using static AddedObjectRemover.CsvFile;

namespace AddedObjectRemover;

/// <param name="OriginFractions">Where the mesh origin sits inside its local bounds per axis: 0 at the minimum, 1 at the maximum; NaN on an axis without size.</param>
internal sealed record MeshOriginRow(
    string ModelPath,
    string BaseEditorIds,
    int ReferenceCount,
    Box LocalBounds,
    Vector3 OriginFractions,
    string Classification);

/// <summary>Counts of meshes per origin classification, for the log.</summary>
internal readonly record struct MeshOriginSummary(int Meshes, int NearBottom, int NearCentre, int Other);

/// <summary>
/// Optional mesh-origins.csv: for every mesh used by target objects, where its origin sits inside
/// its local bounds. Anchoring weights contact points by closeness to the origin, which assumes
/// the origin marks where an object rests; this file shows how often that holds.
/// </summary>
internal static class MeshOriginReport
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

    /// <summary>One row per target mesh, ordered by model path.</summary>
    public static List<MeshOriginRow> CreateRows(IReadOnlyList<TargetObject> targets, BaseObjectShapeProvider shapes) =>
        targets
            .Where(target => shapes.GetMeshPath(target.Base) != null)
            .GroupBy(target => shapes.GetMeshPath(target.Base)!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateRow(group.Key, group.ToList(), shapes))
            .ToList();

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

    private static MeshOriginRow CreateRow(string modelPath, IReadOnlyList<TargetObject> users, BaseObjectShapeProvider shapes)
    {
        var bounds = shapes.GetLocalBox(users[0].Base);
        var fractions = OriginFractions(bounds);
        return new MeshOriginRow(modelPath, DescribeBases(users, shapes), users.Count, bounds, fractions, Classify(fractions.Z));
    }

    private static string DescribeBases(IEnumerable<TargetObject> users, BaseObjectShapeProvider shapes) =>
        string.Join(
            "; ",
            users
                .Select(user => user.Base)
                .OfType<BaseRef>()
                .DistinctBy(reference => reference.FormKey)
                .Select(reference => shapes.ResolveBaseOrNull(reference)?.EditorID ?? reference.FormKey.ToString())
                .Order(StringComparer.OrdinalIgnoreCase));

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
        Num(row.LocalBounds.Min.X), Num(row.LocalBounds.Min.Y), Num(row.LocalBounds.Min.Z),
        Num(row.LocalBounds.Max.X), Num(row.LocalBounds.Max.Y), Num(row.LocalBounds.Max.Z),
        Num(row.OriginFractions.X), Num(row.OriginFractions.Y), Num(row.OriginFractions.Z),
        Text(row.Classification),
    ];
}
