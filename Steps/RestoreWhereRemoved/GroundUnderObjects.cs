using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The LAND grid points under objects: under the object's box grown by one grid step on every side, seen from above, in the
/// worldspace whose land the object's space uses. Thread-safe.
/// </summary>
/// <param name="findLandWorldspace">A space → the worldspace whose land it uses; null for a space without land.</param>
internal sealed class GroundUnderObjects(IBaseObjectShapes shapes, Func<RecordKey, RecordKey?> findLandWorldspace)
{
    /// <returns>None in a space without land.</returns>
    public IEnumerable<LandGridPoint> Under(OtherObject placed) => Under(placed.SpaceKey, placed.Base, placed.Transform);

    /// <param name="heightsOf">The heights of a cell's grid points; null where they are not known.</param>
    /// <returns>The points under the object when the grown box's height range overlaps the known heights there; none otherwise.</returns>
    public IEnumerable<LandGridPoint> UnderWhereItReachesTheGround(TargetObject placed, Func<ExteriorCell, float[]?> heightsOf)
    {
        if (findLandWorldspace(placed.SpaceKey) is not { } land) return [];
        var box = GrownBoxOf(placed.Base, placed.Transform);
        var points = PointsUnder(land, box).ToList();
        var heights = points.Select(point => heightsOf(point.Cell)?[point.Point]).OfType<float>().ToList();
        return heights.Count > 0 && heights.Min() <= box.Max.Z && heights.Max() >= box.Min.Z ? points : [];
    }

    /// <summary>The points under each of the objects, as <see cref="UnderWhereItReachesTheGround"/> finds them.</summary>
    public IEnumerable<LandGridPoint> UnderWhereTheyReachTheGround(IEnumerable<TargetObject> placed, Func<ExteriorCell, float[]?> heightsOf) =>
        placed.SelectMany(each => UnderWhereItReachesTheGround(each, heightsOf));

    /// <summary>The lowest point of the object's box, not grown.</summary>
    public float BottomOf(OtherObject placed) => BaseBoxes.Of(shapes, placed.Base, placed.Transform).WorldAabb(0f).Min.Z;

    private IEnumerable<LandGridPoint> Under(RecordKey spaceKey, BaseKey? model, PlacedTransform transform) =>
        findLandWorldspace(spaceKey) is { } land ? PointsUnder(land, GrownBoxOf(model, transform)) : [];

    private Box GrownBoxOf(BaseKey? model, PlacedTransform transform) => BaseBoxes.Of(shapes, model, transform).WorldAabb(TerrainGrid.VertexSpacing);

    private static IEnumerable<LandGridPoint> PointsUnder(RecordKey land, Box box) =>
        TerrainGrid.VerticesUnder(box).SelectMany(under => under.VertexIndices.Select(point => new LandGridPoint(new ExteriorCell(land, under.CellX, under.CellY), point)));
}
