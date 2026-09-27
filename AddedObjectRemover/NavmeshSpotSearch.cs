using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// Free spots on the winning navmesh: a navmesh point is free when neither it nor any of the points
/// <see cref="IFreeSpotSearch.Clearance"/> away from it along the world X and Y axes lies inside a
/// remaining visible object. Thread-safe.
/// </summary>
internal sealed class NavmeshSpotSearch(NavmeshIndex navmeshes, VisibleObstacles obstacles) : IFreeSpotSearch
{
    private static readonly Vector3[] ClearanceOffsets =
    [
        Vector3.Zero,
        new(IFreeSpotSearch.Clearance, 0, 0),
        new(-IFreeSpotSearch.Clearance, 0, 0),
        new(0, IFreeSpotSearch.Clearance, 0),
        new(0, -IFreeSpotSearch.Clearance, 0),
    ];

    public RelocationSurface Surface => RelocationSurface.Navmesh;

    /// <remarks>Neighboring navmesh triangles offer the same points, so each point is tested once per search.</remarks>
    public bool TryFindNearestFreePoint(
        FormKey spaceKey, Vector3 point, float maxDistance, CellArea? allowedCells, SpatialQueryScratch scratch, out Vector3 found)
    {
        var tested = new Dictionary<Vector3, bool>();
        bool IsFree(Vector3 spot)
        {
            if (tested.TryGetValue(spot, out var free)) return free;
            free = IsAllowed(spaceKey, spot, allowedCells, scratch);
            tested[spot] = free;
            return free;
        }

        return navmeshes.TryFindNearestFreePoint(spaceKey, point, maxDistance, IsFree, out found);
    }

    private bool IsAllowed(FormKey spaceKey, Vector3 spot, CellArea? allowedCells, SpatialQueryScratch scratch) =>
        (allowedCells is not { } cells || cells.ContainsInset(spot, IFreeSpotSearch.Clearance))
        && ClearanceOffsets.All(offset => !obstacles.IsInsideAny(spaceKey, spot + offset, scratch));
}
