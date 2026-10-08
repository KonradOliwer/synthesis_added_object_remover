using System.Numerics;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AddedObjectRemover;

/// <summary>
/// Polygon work on the ground plane, seen from above. Overlays use NetTopologySuite's robust
/// OverlayNG: the default overlay can throw on the nearly coincident edges that neighboring boxes
/// produce.
/// </summary>
public static class FlatArea
{
    private static readonly GeometryFactory Factory = GeometryFactory.Default;

    public static Point CreatePoint(Vector2 point) => Factory.CreatePoint(new Coordinate(point.X, point.Y));

    public static NtsGeometry CreateDisc(Point center, float radius) => center.Buffer(radius);

    public static NtsGeometry CreateRectangle(FlatRectangle rectangle) =>
        Factory.ToGeometry(new Envelope(rectangle.Min.X, rectangle.Max.X, rectangle.Min.Y, rectangle.Max.Y));

    /// <summary>The convex hull of the box corners seen from above; a point or line for a flat or empty box.</summary>
    public static NtsGeometry CreateFootprint(OrientedBox box)
    {
        var corners = box.Corners().Select(corner => new Coordinate(corner.X, corner.Y)).ToArray();
        return Factory.CreateMultiPointFromCoords(corners).ConvexHull();
    }

    public static FlatRectangle BoundsOf(NtsGeometry area)
    {
        var bounds = area.EnvelopeInternal;
        return new FlatRectangle(new Vector2((float)bounds.MinX, (float)bounds.MinY), new Vector2((float)bounds.MaxX, (float)bounds.MaxY));
    }

    public static NtsGeometry Intersect(NtsGeometry area, NtsGeometry other) =>
        OverlayNGRobust.Overlay(area, other, SpatialFunction.Intersection);

    /// <summary>The area without the union of <paramref name="blocked"/>, each blocked part grown by <paramref name="clearance"/>.</summary>
    public static NtsGeometry Subtract(NtsGeometry area, IReadOnlyCollection<NtsGeometry> blocked, float clearance)
    {
        if (blocked.Count == 0) return area;
        var blockedArea = OverlayNGRobust.Union(blocked.ToList()).Buffer(clearance);
        return OverlayNGRobust.Overlay(area, blockedArea, SpatialFunction.Difference);
    }

    public static Vector2 NearestPoint(NtsGeometry area, Point start)
    {
        var nearest = DistanceOp.NearestPoints(area, start)[0];
        return new Vector2((float)nearest.X, (float)nearest.Y);
    }
}
