using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;

namespace AddedObjectRemover.Steps.MoveKeptMarkers;

/// <summary>
/// Finds the nearest point on a worldspace's terrain outside the ground footprint of every
/// remaining visible object: the free area is the disc of the search distance around the point
/// minus the union of the footprints (each object's box projected onto the ground) and of the
/// cells without terrain, each grown by the clearance, and the nearest point of that area is lifted
/// onto the terrain. Thread-safe.
/// </summary>
internal sealed class TerrainSpotSearch(ITerrainHeights terrain, OtherObjectsAroundMarker objectsAroundMarker) : IFreeSpotSearch
{
    public RelocationSurface Surface => RelocationSurface.Terrain;

    public bool TryFindNearestFreePoint(
        RecordKey spaceKey, Vector3 point, float maxDistance, CellArea? allowedCells, ObjectQueryScratch scratch, out Vector3 found)
    {
        found = default;
        if (!terrain.HasTerrain(spaceKey)) return false;

        FlatRectangle? clip = allowedCells is { } cells ? ToRectangle(cells, IFreeSpotSearch.Clearance) : null;
        var nearest = NearestFreePoint.Find(
            new Vector2(point.X, point.Y),
            maxDistance,
            clip,
            searchBounds => FindRectanglesWithoutTerrain(spaceKey, searchBounds),
            objectsAroundMarker.FindBoxesNear(spaceKey, point, maxDistance, scratch),
            IFreeSpotSearch.Clearance);
        if (nearest is not { } position) return false;

        if (!terrain.TryGetHeight(spaceKey, position, out var height)) return false;
        found = new Vector3(position, height);
        return true;
    }

    private IEnumerable<FlatRectangle> FindRectanglesWithoutTerrain(RecordKey spaceKey, FlatRectangle searchBounds)
    {
        var covered = CellArea.Covering(new Box(new Vector3(searchBounds.Min, 0), new Vector3(searchBounds.Max, 0)));
        return covered.Cells()
            .Where(cell => !terrain.HasHeights(spaceKey, cell.X, cell.Y))
            .Select(cell => ToRectangle(CellArea.Single(cell.X, cell.Y), inset: 0f));
    }

    private static FlatRectangle ToRectangle(CellArea cells, float inset)
    {
        var (min, max) = cells.GetInsetRectangle(inset);
        return new FlatRectangle(min, max);
    }
}
