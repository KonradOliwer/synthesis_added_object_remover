using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// For every mesh with triangles used by target objects, where its origin sits inside the bounds of
/// its triangles. Anchoring weights contact points by closeness to the origin, which assumes the
/// origin marks where an object rests; this shows how often that holds.
/// </summary>
internal static class MeshOriginSurvey
{
    public const string NearBottom = "origin near bottom";
    public const string NearCentre = "origin near centre";
    public const string Other = "other";

    private const float BottomMaxFraction = 0.1f;
    private const float CentreMinFraction = 0.35f;
    private const float CentreMaxFraction = 0.65f;

    /// <returns>One per target mesh with usable triangles, ordered by mesh path.</returns>
    public static ImmutableArray<MeshOrigin> Measure(
        IReadOnlyList<TargetObject> targets,
        ShapeCatalog shapes,
        IBaseFacts bases,
        TriangleStore triangles,
        Execution execution)
    {
        var users = targets
            .Where(target => shapes.GetMeshPath(target.Base) != null)
            .GroupBy(target => shapes.GetMeshPath(target.Base)!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bounds = MeasureTriangleBounds(users.Select(group => group.Key).ToList(), triangles, execution);

        var origins = ImmutableArray.CreateBuilder<MeshOrigin>();
        for (var i = 0; i < users.Count; i++)
        {
            if (bounds[i] is { } triangleBounds) origins.Add(Locate(users[i].Key, users[i].ToList(), triangleBounds, bases));
        }
        return origins.ToImmutable();
    }

    /// <returns>Per mesh: the bounds of its triangles, or null when it has no usable triangles.</returns>
    private static Box?[] MeasureTriangleBounds(IReadOnlyList<string> meshPaths, TriangleStore triangles, Execution execution) =>
        ParallelMap.Run(execution, meshPaths.Count, i =>
        {
            using var lease = triangles.Acquire(meshPaths[i]);
            return lease.Tree?.Bounds;
        }, rangeSize: ParallelMap.OneItemPerRange);

    private static MeshOrigin Locate(string mesh, IReadOnlyList<TargetObject> users, Box triangleBounds, IBaseFacts bases)
    {
        var fractions = OriginFractions(triangleBounds);
        return new MeshOrigin(mesh, ListBaseEditorIds(users, bases), users.Count, triangleBounds, fractions, Classify(fractions.Z));
    }

    /// <returns>Each base once, by Editor ID or, without one, by FormKey; sorted ignoring case.</returns>
    private static ImmutableArray<string> ListBaseEditorIds(IEnumerable<TargetObject> users, IBaseFacts bases) =>
    [
        .. users
            .Select(user => user.Base)
            .OfType<BaseRef>()
            .DistinctBy(reference => reference.FormKey)
            .Select(reference => NameBase(bases.Of(reference), reference))
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private static string NameBase(BaseFacts facts, BaseRef reference) =>
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
}
