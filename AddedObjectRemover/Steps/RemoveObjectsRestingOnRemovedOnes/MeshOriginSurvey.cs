using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// For every mesh with triangles used by target objects, where its origin sits inside the bounds of
/// its triangles. Anchoring weights contact points by closeness to the origin, which assumes the
/// origin marks where an object rests; this shows how often that holds.
/// </summary>
internal static class MeshOriginSurvey
{
    private const float BottomMaxFraction = 0.1f;
    private const float CentreMinFraction = 0.35f;
    private const float CentreMaxFraction = 0.65f;

    /// <returns>One per target mesh with usable triangles, ordered by mesh path.</returns>
    public static ImmutableArray<MeshOrigin> Measure(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IBaseFacts bases,
        ITriangleMeshes triangles,
        Execution execution)
    {
        var withMesh = targets.Where(target => shapes.Of(target.Base).MeshPath != null).ToList();
        var meshes = KeyedGroups
            .IndicesByKey(withMesh, target => shapes.Of(target.Base).MeshPath!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(mesh => mesh.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bounds = MeasureTriangleBounds(meshes.Select(mesh => mesh.Key).ToList(), triangles, execution);

        var origins = ImmutableArray.CreateBuilder<MeshOrigin>();
        for (var i = 0; i < meshes.Count; i++)
        {
            if (bounds[i] is not { } triangleBounds) continue;
            var users = meshes[i].Indices.Select(index => withMesh[index]).ToList();
            origins.Add(Locate(meshes[i].Key, users, triangleBounds, bases));
        }
        return origins.ToImmutable();
    }

    /// <returns>Per mesh: the bounds of its triangles, or null when it has no usable triangles.</returns>
    private static Box?[] MeasureTriangleBounds(IReadOnlyList<string> meshPaths, ITriangleMeshes triangles, Execution execution) =>
        ParallelMap.Run(execution, meshPaths.Count, i =>
        {
            using var lease = triangles.Acquire(meshPaths[i]);
            return lease.Value?.Bounds;
        }, rangeSize: ParallelMap.OneItemPerRange);

    private static MeshOrigin Locate(string mesh, IReadOnlyList<TargetObject> users, Box triangleBounds, IBaseFacts bases)
    {
        var fractions = Boxes.OriginFraction(triangleBounds);
        return new MeshOrigin(mesh, ListBaseEditorIds(users, bases), users.Count, triangleBounds, fractions, Classify(fractions.Z));
    }

    /// <returns>Each base once, by Editor ID or, without one, by record key; sorted ignoring case.</returns>
    private static ImmutableArray<string> ListBaseEditorIds(IEnumerable<TargetObject> users, IBaseFacts bases) =>
    [
        .. KeyedGroups
            .DistinctBy(users.Select(user => user.Base).OfType<BaseKey>(), baseKey => baseKey.Record, EqualityComparer<RecordKey>.Default)
            .Select(baseKey => NameBase(bases.Of(baseKey), baseKey))
            .Order(StringComparer.OrdinalIgnoreCase),
    ];

    private static string NameBase(BaseFacts facts, BaseKey baseKey) =>
        (facts.Resolved ? facts.EditorId : null) ?? baseKey.Record.ToString();

    private static string Classify(float zFraction)
    {
        if (zFraction <= BottomMaxFraction) return MeshOriginPlaces.NearBottom;
        return zFraction is >= CentreMinFraction and <= CentreMaxFraction ? MeshOriginPlaces.NearCentre : MeshOriginPlaces.Other;
    }
}
