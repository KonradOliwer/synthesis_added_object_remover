using System.Numerics;
using Mutagen.Bethesda.Plugins;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AddedObjectRemover;

/// <summary>
/// Finds the nearest point on a worldspace's terrain outside the ground footprint of every
/// remaining visible object: the free area is the disc of the search distance around the point
/// minus the union of the footprints (each obstacle's box projected onto the ground), and the
/// nearest point of that area is lifted onto the terrain. Thread-safe.
/// </summary>
internal sealed class TerrainSpotSearch(TerrainHeights terrain, VisibleObstacles obstacles)
{
    /// <summary>Distance, in game units, kept from every footprint, so the spot is not on an obstacle's edge.</summary>
    private const double FootprintClearance = 16;

    private static readonly GeometryFactory Factory = GeometryFactory.Default;

    /// <param name="requiredCell">The exterior cell the point must stay in; null when it may move anywhere.</param>
    public bool TryFindNearestFreePoint(FormKey spaceKey, Vector3 point, float maxDistance, (int X, int Y)? requiredCell, out Vector3 found)
    {
        found = default;
        if (!terrain.HasTerrain(spaceKey)) return false;

        var start = Factory.CreatePoint(new Coordinate(point.X, point.Y));
        var freeArea = FindFreeArea(spaceKey, point, start, maxDistance, requiredCell);
        if (freeArea.IsEmpty) return false;

        var nearest = DistanceOp.NearestPoints(freeArea, start)[0];
        var position = new Vector2((float)nearest.X, (float)nearest.Y);
        if (!terrain.TryGetHeight(spaceKey, position, out var height)) return false;
        found = new Vector3(position, height);
        return Vector3.Distance(point, found) <= maxDistance;
    }

    private NtsGeometry FindFreeArea(FormKey spaceKey, Vector3 point, Point start, float maxDistance, (int X, int Y)? requiredCell)
    {
        var searchArea = start.Buffer(maxDistance);
        if (requiredCell is { } cell) searchArea = searchArea.Intersection(CreateCellSquare(cell));

        var footprints = obstacles.FindBoxesNear(spaceKey, point, maxDistance).Select(CreateFootprint).ToList();
        if (footprints.Count == 0) return searchArea;
        var blockedArea = Factory.BuildGeometry(footprints).Union().Buffer(FootprintClearance);
        return searchArea.Difference(blockedArea);
    }

    /// <remarks>Inset by the clearance, as a point on the square's north or east edge already belongs to the next cell.</remarks>
    private static NtsGeometry CreateCellSquare((int X, int Y) cell)
    {
        var minX = cell.X * (double)ExteriorGrid.CellSize + FootprintClearance;
        var minY = cell.Y * (double)ExteriorGrid.CellSize + FootprintClearance;
        var width = ExteriorGrid.CellSize - 2 * FootprintClearance;
        return Factory.ToGeometry(new Envelope(minX, minX + width, minY, minY + width));
    }

    /// <summary>The convex hull of the box corners seen from above; a point or line for a flat or empty box.</summary>
    private static NtsGeometry CreateFootprint(OrientedBox box)
    {
        var corners = box.Corners().Select(corner => new Coordinate(corner.X, corner.Y)).ToArray();
        return Factory.CreateMultiPointFromCoords(corners).ConvexHull();
    }
}
