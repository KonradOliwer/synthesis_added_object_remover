using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Finds the nearest free point on a worldspace's terrain, sampled on an even grid. Thread-safe.</summary>
internal sealed class TerrainSpotSearch(TerrainHeights terrain)
{
    /// <summary>Distance between sampled terrain points, in game units (half the terrain's own vertex spacing).</summary>
    private const float SampleSpacing = 64f;

    /// <summary>
    /// Samples square rings around the point, nearest ring first. A ring's horizontal distance is
    /// a lower bound of its points' distance, so the search stops once no ring can hold a closer point.
    /// </summary>
    public bool TryFindNearestFreePoint(FormKey spaceKey, Vector3 point, float maxDistance, Func<Vector3, bool> isFree, out Vector3 found)
    {
        found = default;
        if (!terrain.HasTerrain(spaceKey)) return false;

        var bestDistance = maxDistance;
        var center = new Vector2(point.X, point.Y);
        for (var ring = 0; ring * SampleSpacing < bestDistance; ring++)
        {
            foreach (var position in EnumerateRing(center, ring))
            {
                if (!terrain.TryGetHeight(spaceKey, position, out var height)) continue;
                var candidate = new Vector3(position, height);
                var distance = Vector3.Distance(point, candidate);
                if (distance >= bestDistance || !isFree(candidate)) continue;
                found = candidate;
                bestDistance = distance;
            }
        }
        return bestDistance < maxDistance;
    }

    /// <summary>The sample positions whose larger axis offset from the centre is <paramref name="ring"/> samples.</summary>
    private static IEnumerable<Vector2> EnumerateRing(Vector2 center, int ring)
    {
        if (ring == 0)
        {
            yield return center;
            yield break;
        }
        for (var i = -ring; i <= ring; i++)
        {
            yield return center + new Vector2(i, -ring) * SampleSpacing;
            yield return center + new Vector2(i, ring) * SampleSpacing;
        }
        for (var j = -ring + 1; j < ring; j++)
        {
            yield return center + new Vector2(-ring, j) * SampleSpacing;
            yield return center + new Vector2(ring, j) * SampleSpacing;
        }
    }
}
