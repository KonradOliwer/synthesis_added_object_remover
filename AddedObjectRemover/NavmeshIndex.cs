using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Winning navmesh triangles (world space) of each space, for finding the nearest point on them
/// that is free. The navmeshes of one exterior cell, or of all of a space outside the exterior
/// grid, are decoded and indexed on the first query that reaches them. Thread-safe.
/// </summary>
internal sealed class NavmeshIndex
{
    /// <summary>Points tried per triangle side when the triangle's closest point is not free.</summary>
    private const int SamplesPerSide = 8;

    /// <param name="Grid">The exterior cell; null for the space's navmeshes outside the exterior grid.</param>
    private readonly record struct Bucket(FormKey SpaceKey, (int X, int Y)? Grid);

    private sealed record BucketTriangles(MeshTriangle[] Triangles, SpatialGrid Grid);

    private readonly record struct Candidate(Vector3 Point, float Distance);

    private readonly Dictionary<Bucket, INavigationMeshDataGetter[]> _navmeshes;
    private readonly LazyCache<Bucket, BucketTriangles> _triangles = new();

    public NavmeshIndex(IReadOnlyDictionary<FormKey, List<CellNavmesh>> navmeshesBySpace)
    {
        _navmeshes = navmeshesBySpace
            .SelectMany(space => space.Value.Select(navmesh => (Bucket: new Bucket(space.Key, navmesh.Grid), navmesh.Data)))
            .GroupBy(entry => entry.Bucket, entry => entry.Data)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

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

    private List<(MeshTriangle Triangle, float Distance)> FindTrianglesByDistance(FormKey spaceKey, Vector3 point, float maxDistance)
    {
        var query = new Box(point, point).Grown(maxDistance);
        return EnumerateBuckets(spaceKey, query)
            .Where(_navmeshes.ContainsKey)
            .SelectMany(bucket => FindTrianglesIn(_triangles.GetOrCreate(bucket, () => DecodeBucket(bucket)), query))
            .Select(triangle => (Triangle: triangle, Distance: Vector3.Distance(point, ClosestPoint(triangle, point))))
            .Where(entry => entry.Distance < maxDistance)
            .OrderBy(entry => entry.Distance)
            .ToList();
    }

    /// <summary>An exterior cell's navmesh lies within the cell's square, so only the cells overlapping the query can hold a triangle in it.</summary>
    private static IEnumerable<Bucket> EnumerateBuckets(FormKey spaceKey, Box query) =>
        CellArea.Covering(query).Cells().Select(cell => new Bucket(spaceKey, cell)).Prepend(new Bucket(spaceKey, Grid: null));

    private BucketTriangles DecodeBucket(Bucket bucket)
    {
        var triangles = _navmeshes[bucket].SelectMany(NavmeshTriangles.Read).ToArray();
        return new BucketTriangles(triangles, SpatialGrid.FromBoxes(triangles.Select(triangle => triangle.Bounds).ToArray()));
    }

    /// <summary>Triangles can be indexed in several grid cells, so duplicates are removed.</summary>
    private static IEnumerable<MeshTriangle> FindTrianglesIn(BucketTriangles bucket, Box query)
    {
        var candidates = new List<int>();
        bucket.Grid.Collect(query, candidates);
        return candidates.Distinct().Select(index => bucket.Triangles[index]);
    }

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

    /// <remarks>The corners are returned exactly, so triangles sharing a vertex offer the identical point.</remarks>
    private static IEnumerable<Vector3> EnumeratePointsOn(MeshTriangle triangle, Vector3 point)
    {
        yield return ClosestPoint(triangle, point);
        var toB = (triangle.B - triangle.A) / SamplesPerSide;
        var toC = (triangle.C - triangle.A) / SamplesPerSide;
        for (var i = 0; i <= SamplesPerSide; i++)
        {
            for (var j = 0; i + j <= SamplesPerSide; j++)
            {
                yield return (i, j) switch
                {
                    (SamplesPerSide, _) => triangle.B,
                    (_, SamplesPerSide) => triangle.C,
                    _ => triangle.A + toB * i + toC * j,
                };
            }
        }
    }

    private static Vector3 ClosestPoint(MeshTriangle triangle, Vector3 point) =>
        Geometry.ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
}
