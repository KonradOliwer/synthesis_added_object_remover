using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The grid points the terrain plan keeps for what stays besides the cleaned mod's objects: the navmeshes lying on the ground as it is
/// where they would not lie on the ground that goes back. Thread-safe.
/// </summary>
/// <param name="spacesOnLand">Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</param>
/// <param name="heightTolerance">A navmesh closer to the ground than this lies on it.</param>
internal sealed class TerrainGuards(IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> spacesOnLand, float heightTolerance, Execution execution)
{
    /// <summary>Half the width of the column a grid point is probed with, so a triangle edge running through the point is found.</summary>
    private const float ProbeHalfWidth = 1f;

    /// <summary>
    /// Every record's point at each world grid point that would go back where a navmesh as it will stand lies within the height
    /// tolerance of the ground as it is but not of the ground once written, in the order of the points.
    /// </summary>
    public IEnumerable<LandGridPoint> UnderNavmeshesOnTheGroundAsItIs(TerrainUndoPlan plan, NavmeshesAsTheyWillStand navmeshes)
    {
        var restored = plan.RestoredPoints().ToList();
        return ParallelMap.Run(execution, restored.Count, index => PointsKeptUnderNavmeshes(restored[index], navmeshes), ParallelMap.AutomaticRangeSize)
            .SelectMany(points => points);
    }

    private List<LandGridPoint> PointsKeptUnderNavmeshes(RestoredGridPoint point, NavmeshesAsTheyWillStand navmeshes) =>
        LiesOnTheGroundAsItIs(point, navmeshes) ? [.. point.Point.Holders()] : [];

    private bool LiesOnTheGroundAsItIs(RestoredGridPoint point, NavmeshesAsTheyWillStand navmeshes) =>
        spacesOnLand.GetValueOrDefault(point.Point.Worldspace, []).Any(space =>
            navmeshes.AnyTriangleIn(space, ProbeAt(point.Point, point.Current)) && !navmeshes.AnyTriangleIn(space, ProbeAt(point.Point, point.Planned)));

    /// <summary>A thin column around the point reaching the height tolerance above and below the height.</summary>
    private OrientedBox ProbeAt(WorldGridPoint point, float height) =>
        new(new Vector3(point.X * TerrainGrid.VertexSpacing, point.Y * TerrainGrid.VertexSpacing, height), Mat3.Identity, new Vector3(ProbeHalfWidth, ProbeHalfWidth, heightTolerance));
}
