using System.Numerics;
using Mutagen.Bethesda.Plugins;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AddedObjectRemover;

/// <summary>
/// Finds the nearest point on a worldspace's terrain outside the ground footprint of every
/// remaining visible object: the free area is the disc of the search distance around the point
/// minus the union of the footprints (each obstacle's box projected onto the ground) and of the
/// cells without terrain, each grown by the clearance, and the nearest point of that area is lifted
/// onto the terrain. Overlays use NetTopologySuite's robust OverlayNG: the default overlay can throw
/// on the nearly coincident edges that neighboring boxes produce. Thread-safe.
/// </summary>
internal sealed class TerrainSpotSearch(TerrainHeights terrain, VisibleObstacles obstacles) : IFreeSpotSearch
{
    private static readonly GeometryFactory Factory = GeometryFactory.Default;

    public RelocationSurface Surface => RelocationSurface.Terrain;

    public bool TryFindNearestFreePoint(FormKey spaceKey, Vector3 point, float maxDistance, CellArea? allowedCells, out Vector3 found)
    {
        found = default;
        if (!terrain.HasTerrain(spaceKey)) return false;

        var start = Factory.CreatePoint(new Coordinate(point.X, point.Y));
        var freeArea = FindFreeArea(spaceKey, point, start, maxDistance, allowedCells);
        if (freeArea.IsEmpty) return false;

        var nearest = DistanceOp.NearestPoints(freeArea, start)[0];
        var position = new Vector2((float)nearest.X, (float)nearest.Y);
        if (!terrain.TryGetHeight(spaceKey, position, out var height)) return false;
        found = new Vector3(position, height);
        return true;
    }

    private NtsGeometry FindFreeArea(FormKey spaceKey, Vector3 point, Point start, float maxDistance, CellArea? allowedCells)
    {
        var searchArea = start.Buffer(maxDistance);
        if (allowedCells is { } cells)
        {
            searchArea = OverlayNGRobust.Overlay(searchArea, CreateCellsRectangle(cells, IFreeSpotSearch.Clearance), SpatialFunction.Intersection);
            if (searchArea.IsEmpty) return searchArea;
        }

        var blocked = CollectFootprints(spaceKey, point, maxDistance)
            .Concat(FindCellsWithoutTerrain(spaceKey, searchArea.EnvelopeInternal).Select(cell => CreateCellsRectangle(cell, inset: 0)))
            .ToList();
        if (blocked.Count == 0) return searchArea;
        var blockedArea = OverlayNGRobust.Union(blocked).Buffer(IFreeSpotSearch.Clearance);
        return OverlayNGRobust.Overlay(searchArea, blockedArea, SpatialFunction.Difference);
    }

    private IEnumerable<NtsGeometry> CollectFootprints(FormKey spaceKey, Vector3 point, float maxDistance) =>
        obstacles.FindBoxesNear(spaceKey, point, maxDistance).Select(CreateFootprint);

    private IEnumerable<CellArea> FindCellsWithoutTerrain(FormKey spaceKey, Envelope area)
    {
        var covered = CellArea.Covering(new Box(
            new Vector3((float)area.MinX, (float)area.MinY, 0),
            new Vector3((float)area.MaxX, (float)area.MaxY, 0)));
        return covered.Cells()
            .Where(cell => !terrain.HasHeights(spaceKey, cell.X, cell.Y))
            .Select(cell => CellArea.Single(cell.X, cell.Y));
    }

    /// <param name="inset">Distance kept from the rectangle's border; for allowed cells, a point on the north or east border already belongs to the next cell.</param>
    private static NtsGeometry CreateCellsRectangle(CellArea cells, double inset)
    {
        var minX = cells.MinX * (double)ExteriorGrid.CellSize + inset;
        var minY = cells.MinY * (double)ExteriorGrid.CellSize + inset;
        var maxX = (cells.MaxX + 1) * (double)ExteriorGrid.CellSize - inset;
        var maxY = (cells.MaxY + 1) * (double)ExteriorGrid.CellSize - inset;
        return Factory.ToGeometry(new Envelope(minX, maxX, minY, maxY));
    }

    /// <summary>The convex hull of the box corners seen from above; a point or line for a flat or empty box.</summary>
    private static NtsGeometry CreateFootprint(OrientedBox box)
    {
        var corners = box.Corners().Select(corner => new Coordinate(corner.X, corner.Y)).ToArray();
        return Factory.CreateMultiPointFromCoords(corners).ConvexHull();
    }
}
