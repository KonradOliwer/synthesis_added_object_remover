using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Winning navmesh triangles (world space) of each space, for finding the nearest point on them
/// that is free. The navmeshes of one exterior cell, or of all of a space outside the exterior
/// grid, are read and indexed on the first query that reaches them. Thread-safe.
/// </summary>
/// <param name="readNavmeshes">The triangles of a bucket; null where the bucket has no navmesh.</param>
public sealed class NavmeshIndex(Func<NavmeshBucket, MeshTriangle[]?> readNavmeshes) : INavmeshes
{
    /// <summary>Points tried per triangle side when the triangle's closest point is not free.</summary>
    private const int SamplesPerSide = 8;

    private sealed record BucketTriangles(MeshTriangle[] Triangles, SpatialGrid Grid);

    private readonly record struct Candidate(Vector3 Point, float Distance);

    private readonly ComputedOncePerKey<NavmeshBucket, BucketTriangles?> _triangles = new(Publication.BuiltOnce, EqualityComparer<NavmeshBucket>.Default);

    /// <summary>
    /// The navmesh point nearest to <paramref name="point"/>, within <paramref name="maxDistance"/>,
    /// that <paramref name="isFree"/> accepts. Each triangle offers its closest point and an even
    /// grid of points on it; triangles are visited nearest first and the search stops once no
    /// closer triangle is left.
    /// </summary>
    public bool TryFindNearestFreePoint(RecordKey spaceKey, Vector3 point, float maxDistance, Func<Vector3, bool> isFree, out Vector3 found)
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

    private List<(MeshTriangle Triangle, float Distance)> FindTrianglesByDistance(RecordKey spaceKey, Vector3 point, float maxDistance)
    {
        var query = new Box(point, point).Grown(maxDistance);
        return EnumerateBuckets(spaceKey, query)
            .Select(bucket => _triangles.Get(bucket, () => IndexBucket(bucket)))
            .OfType<BucketTriangles>()
            .SelectMany(bucket => FindTrianglesIn(bucket, query))
            .Select(triangle => (Triangle: triangle, Distance: Vector3.Distance(point, ClosestPoint(triangle, point))))
            .Where(entry => entry.Distance < maxDistance)
            .OrderBy(entry => entry.Distance)
            .ToList();
    }

    /// <summary>An exterior cell's navmesh lies within the cell's square, so only the cells overlapping the query can hold a triangle in it.</summary>
    private static IEnumerable<NavmeshBucket> EnumerateBuckets(RecordKey spaceKey, Box query) =>
        CellArea.Covering(query).Cells().Select(cell => new NavmeshBucket(spaceKey, cell)).Prepend(new NavmeshBucket(spaceKey, Grid: null));

    private BucketTriangles? IndexBucket(NavmeshBucket bucket)
    {
        if (readNavmeshes(bucket) is not { } triangles) return null;
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
        TriangleDistance.ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
}
