using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Items found by grid cell alone, with no test of their own boxes: the candidates are exactly the
/// items whose grid cells the query area touches. A caller's exact test decides the rest. Read-only
/// after construction and safe to query from many threads at once.
/// </summary>
public sealed class GridCandidates
{
    private readonly SpatialGrid _grid;

    private GridCandidates(SpatialGrid grid)
    {
        _grid = grid;
    }

    /// <param name="boxes">Item i is indexed by box i.</param>
    public static GridCandidates OfBoxes(IReadOnlyList<Box> boxes) => new(SpatialGrid.FromBoxes(boxes));

    /// <param name="points">Item i is indexed by point i.</param>
    public static GridCandidates OfPoints(IReadOnlyList<Vector3> points) => new(SpatialGrid.FromPoints(points));

    /// <summary>Replaces <paramref name="into"/> with the ascending, distinct items whose grid cells touch <paramref name="area"/>.</summary>
    public void CollectNear(Box area, List<int> into) => _grid.CollectDistinct(area, into);

    /// <summary>Replaces <paramref name="into"/> with the ascending, distinct items whose grid cells touch the cube of half-side <paramref name="radius"/> around <paramref name="point"/>.</summary>
    public void CollectNear(Vector3 point, float radius, List<int> into) => CollectNear(new Box(point, point).Grown(radius), into);

    /// <summary>Replaces <paramref name="into"/> with the candidates near <paramref name="point"/> that pass <paramref name="exact"/>, still ascending.</summary>
    public void CollectWithinRadius(Vector3 point, float radius, Func<int, bool> exact, List<int> into)
    {
        CollectNear(point, radius, into);
        into.RemoveAll(item => !exact(item));
    }
}
