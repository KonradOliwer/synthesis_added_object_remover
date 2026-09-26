using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Winning navmesh triangles (world space) of each space, for finding the nearest point on them
/// that is free. A space is indexed on its first query. Thread-safe.
/// </summary>
internal sealed class NavmeshIndex(IReadOnlyDictionary<FormKey, List<MeshTriangle>> trianglesBySpace)
{
    /// <summary>Points tried per triangle side when the triangle's closest point is not free.</summary>
    private const int SamplesPerSide = 8;

    private sealed record SpaceTriangles(MeshTriangle[] Triangles, SpatialGrid Grid);

    private readonly record struct Candidate(Vector3 Point, float Distance);

    private readonly LazyCache<FormKey, SpaceTriangles> _bySpace = new();

    /// <summary>Triangles of one navmesh record; triangles naming a missing vertex (broken data) are skipped.</summary>
    public static IEnumerable<MeshTriangle> ReadTriangles(INavigationMeshDataGetter data)
    {
        var vertices = data.Vertices.Select(Geometry.ToVector).ToArray();
        foreach (var triangle in data.Triangles)
        {
            var corners = triangle.Vertices;
            if (!IsVertex(corners.X, vertices) || !IsVertex(corners.Y, vertices) || !IsVertex(corners.Z, vertices)) continue;
            yield return new MeshTriangle(vertices[corners.X], vertices[corners.Y], vertices[corners.Z]);
        }
    }

    private static bool IsVertex(short index, Vector3[] vertices) => index >= 0 && index < vertices.Length;

    /// <summary>
    /// The navmesh point nearest to <paramref name="point"/>, within <paramref name="maxDistance"/>,
    /// that <paramref name="isFree"/> accepts. Each triangle offers its closest point and an even
    /// grid of points on it; triangles are visited nearest first and the search stops once no
    /// closer triangle is left.
    /// </summary>
    public bool TryFindNearestFreePoint(FormKey spaceKey, Vector3 point, float maxDistance, Func<Vector3, bool> isFree, out Vector3 found)
    {
        found = default;
        var bestDistance = maxDistance;
        foreach (var (triangle, lowerBound) in FindTrianglesByDistance(spaceKey, point, maxDistance))
        {
            if (lowerBound >= bestDistance) break;
            var candidate = FindNearestFreePointOn(triangle, point, bestDistance, isFree);
            if (candidate is not { } free) continue;
            found = free.Point;
            bestDistance = free.Distance;
        }
        return bestDistance < maxDistance;
    }

    private IEnumerable<(MeshTriangle Triangle, float Distance)> FindTrianglesByDistance(FormKey spaceKey, Vector3 point, float maxDistance)
    {
        if (!trianglesBySpace.TryGetValue(spaceKey, out var triangles)) return [];
        var space = _bySpace.GetOrCreate(spaceKey, () => IndexSpace(triangles));
        var candidates = new List<int>();
        space.Grid.Collect(new Box(point, point).Grown(maxDistance), candidates);
        return candidates
            .Distinct()
            .Select(index => space.Triangles[index])
            .Select(triangle => (Triangle: triangle, Distance: Vector3.Distance(point, ClosestPoint(triangle, point))))
            .Where(entry => entry.Distance < maxDistance)
            .OrderBy(entry => entry.Distance)
            .ToList();
    }

    private static SpaceTriangles IndexSpace(List<MeshTriangle> triangles) =>
        new(triangles.ToArray(), SpatialGrid.FromBoxes(triangles.Select(triangle => triangle.Bounds).ToArray()));

    private static Candidate? FindNearestFreePointOn(MeshTriangle triangle, Vector3 point, float withinDistance, Func<Vector3, bool> isFree)
    {
        var byDistance = EnumeratePointsOn(triangle, point)
            .Select(candidate => new Candidate(candidate, Vector3.Distance(point, candidate)))
            .Where(candidate => candidate.Distance < withinDistance)
            .OrderBy(candidate => candidate.Distance);
        foreach (var candidate in byDistance)
        {
            if (isFree(candidate.Point)) return candidate;
        }
        return null;
    }

    private static IEnumerable<Vector3> EnumeratePointsOn(MeshTriangle triangle, Vector3 point)
    {
        yield return ClosestPoint(triangle, point);
        var toB = (triangle.B - triangle.A) / SamplesPerSide;
        var toC = (triangle.C - triangle.A) / SamplesPerSide;
        for (var i = 0; i <= SamplesPerSide; i++)
        {
            for (var j = 0; i + j <= SamplesPerSide; j++) yield return triangle.A + toB * i + toC * j;
        }
    }

    private static Vector3 ClosestPoint(MeshTriangle triangle, Vector3 point) =>
        Geometry.ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
}
