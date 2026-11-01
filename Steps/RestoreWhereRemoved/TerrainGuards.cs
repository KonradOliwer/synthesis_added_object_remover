using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The grid points the terrain plan keeps for what stays besides the cleaned mod's objects: the objects of other plugins placed for
/// the ground as it is (G2), and the navmeshes lying on the ground as it is where they would not lie on the ground that goes back (G3).
/// Thread-safe.
/// </summary>
/// <param name="listOtherObjects">
/// The objects of other plugins that stay where they stand, but the placed records whose areas let them go back, in key order;
/// listed and indexed only once some ground goes back.
/// </param>
/// <param name="spacesOnLand">Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</param>
/// <param name="winningHeightsOf">The winning heights of a cell's LAND record; null where it has none.</param>
/// <param name="heightTolerance">A navmesh closer to the ground than this lies on it.</param>
internal sealed class TerrainGuards(
    Func<IReadOnlyList<OtherObject>> listOtherObjects,
    GroundUnderObjects ground,
    IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> spacesOnLand,
    Func<ExteriorCell, float[]?> winningHeightsOf,
    float heightTolerance,
    Execution execution)
{
    /// <summary>Half the width of the column a grid point is probed with, so a triangle edge running through the point is found.</summary>
    private const float ProbeHalfWidth = 1f;

    /// <summary>The objects, and per cell the indexes of those with a grid point under them in that cell, ascending.</summary>
    private readonly ComputedOnce<(IReadOnlyList<OtherObject> Objects, Dictionary<ExteriorCell, List<int>> ByCell)> _objects =
        new(() => IndexByCell(listOtherObjects(), ground, execution));

    /// <summary>
    /// G2: the points under each object that stands in the final world and some of whose ground would go back, when its bottom lies as
    /// close to the lowest ground under it as it is as to the lowest ground once written, or closer: it was placed for the ground as it
    /// is. In key order of the objects.
    /// </summary>
    /// <param name="endsRemoved">Whether a placed record ends removed in the final world.</param>
    /// <remarks>Only the objects with a grid point in a cell holding a restored point are looked at; no other has one under it.</remarks>
    public IEnumerable<LandGridPoint> UnderObjectsPlacedForTheGroundAsItIs(TerrainUndoPlan plan, Func<RecordKey, bool> endsRemoved)
    {
        var cells = plan.CellsWithRestoredPoints().ToList();
        if (cells.Count == 0) return [];
        var (objects, byCell) = _objects.Value;
        List<int> near = [.. cells.SelectMany(cell => byCell.GetValueOrDefault(cell, [])).Distinct().Order().Where(index => !endsRemoved(objects[index].Key))];
        return ParallelMap.Run(execution, near.Count, index => PointsKeptFor(objects[near[index]], plan), ParallelMap.AutomaticRangeSize).SelectMany(points => points);
    }

    /// <summary>
    /// G3: every record's point at each world grid point that would go back where a navmesh as it will stand lies within the height
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

    private static (IReadOnlyList<OtherObject>, Dictionary<ExteriorCell, List<int>>) IndexByCell(IReadOnlyList<OtherObject> objects, GroundUnderObjects ground, Execution execution)
    {
        var cellsOf = ParallelMap.Run(execution, objects.Count, index => ground.Under(objects[index]).Select(point => point.Cell).Distinct().ToList(), ParallelMap.AutomaticRangeSize);
        var byCell = new Dictionary<ExteriorCell, List<int>>();
        for (var index = 0; index < cellsOf.Length; index++)
        {
            foreach (var cell in cellsOf[index])
            {
                if (!byCell.TryGetValue(cell, out var indexes)) byCell.Add(cell, indexes = []);
                indexes.Add(index);
            }
        }
        return (objects, byCell);
    }

    private List<LandGridPoint> PointsKeptFor(OtherObject placed, TerrainUndoPlan plan)
    {
        var points = ground.Under(placed).ToList();
        if (!points.Any(plan.Restores)) return [];
        var bottom = ground.BottomOf(placed);
        // Points under the object always include one that goes back, so some height is known.
        var lowestNow = LandGridPoint.LowestHeight(points, winningHeightsOf);
        var lowestAfter = LandGridPoint.LowestHeight(points, cell => plan.PlannedHeightsOf(cell) ?? winningHeightsOf(cell));
        return MathF.Abs(bottom - lowestNow) <= MathF.Abs(bottom - lowestAfter) ? points : [];
    }

    private bool LiesOnTheGroundAsItIs(RestoredGridPoint point, NavmeshesAsTheyWillStand navmeshes) =>
        spacesOnLand.GetValueOrDefault(point.Point.Worldspace, []).Any(space =>
            navmeshes.AnyTriangleIn(space, ProbeAt(point.Point, point.Current)) && !navmeshes.AnyTriangleIn(space, ProbeAt(point.Point, point.Planned)));

    /// <summary>A thin column around the point reaching the height tolerance above and below the height.</summary>
    private OrientedBox ProbeAt(WorldGridPoint point, float height) =>
        new(new Vector3(point.X * TerrainGrid.VertexSpacing, point.Y * TerrainGrid.VertexSpacing, height), Mat3.Identity, new Vector3(ProbeHalfWidth, ProbeHalfWidth, heightTolerance));
}
