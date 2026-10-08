using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A box's three sizes, ordered from the largest to the smallest.</summary>
public readonly record struct SortedDimensions(float Largest, float Middle, float Smallest);

public static class Boxes
{
    /// <summary>AABB enclosing the box of the given half extents, rotated by <paramref name="rotation"/> and centered at <paramref name="center"/>.</summary>
    public static Box RotatedAabb(Vector3 center, Mat3 rotation, Vector3 halfExtents)
    {
        var rotatedHalfExtents = rotation.AbsTransform(halfExtents);
        return new Box(center - rotatedHalfExtents, center + rotatedHalfExtents);
    }

    /// <summary>Half of the larger of the box's X and Y sizes.</summary>
    public static float HorizontalHalfSize(Vector3 size) => MathF.Max(size.X, size.Y) * 0.5f;

    /// <summary>
    /// Scales a local box by <paramref name="scale"/> and grows it on every side by
    /// <paramref name="multiplier"/> times its (scaled) size along that axis.
    /// </summary>
    public static Box ExpandedLocalBox(Box local, float scale, float multiplier)
    {
        var scaled = local.Scaled(scale);
        var padding = scaled.Size * multiplier;
        return new Box(scaled.Min - padding, scaled.Max + padding);
    }

    /// <summary>The box's sizes multiplied by <paramref name="scale"/>, largest first; assumes a non-negative scale.</summary>
    public static SortedDimensions SortedScaledDimensions(Box local, float scale)
    {
        var size = local.Size * scale;
        var dimensions = new[] { size.X, size.Y, size.Z };
        Array.Sort(dimensions);
        return new SortedDimensions(dimensions[2], dimensions[1], dimensions[0]);
    }

    /// <summary>The AABB of a local box placed at <paramref name="position"/> turned by <paramref name="rotation"/> (no scale).</summary>
    public static Box WorldAabb(Box local, Vector3 position, Mat3 rotation) =>
        RotatedAabb(position + rotation.Transform(local.Center), rotation, local.HalfExtents);

    /// <summary>Inclusive test in the box's own frame: the world point carried back by <paramref name="position"/> and <paramref name="rotation"/> lies in <paramref name="localBox"/> (min/max comparison).</summary>
    public static bool IsInsideOrientedBox(Vector3 worldPoint, Vector3 position, Mat3 rotation, Box localBox) =>
        localBox.Contains(rotation.TransformTransposed(worldPoint - position));

    public static Vector3 WorldBoundsCenter(Box local, PlacedTransform transform) =>
        transform.Position + transform.Rotation.Transform(local.Center * transform.Scale);

    /// <summary>Per axis where the origin sits inside the box: 0 at the minimum, 1 at the maximum; NaN on an axis without size.</summary>
    public static Vector3 OriginFraction(Box box) => new(
        OriginFractionOnAxis(box.Min.X, box.Max.X),
        OriginFractionOnAxis(box.Min.Y, box.Max.Y),
        OriginFractionOnAxis(box.Min.Z, box.Max.Z));

    /// <summary>The distance from the point to the box ignoring height; zero over or inside the box's footprint.</summary>
    public static float HorizontalDistance(Box box, Vector3 point)
    {
        var belowMin = new Vector2(box.Min.X - point.X, box.Min.Y - point.Y);
        var aboveMax = new Vector2(point.X - box.Max.X, point.Y - box.Max.Y);
        return Vector2.Max(Vector2.Zero, Vector2.Max(belowMin, aboveMax)).Length();
    }

    /// <summary>
    /// world = position + R × (scale × (centre + factor × (x − centre))), rewritten as a placement:
    /// position + R × (scale × (1 − factor) × centre) + R × (scale × factor × x).
    /// </summary>
    public static PlacedTransform EnlargeAroundCentre(PlacedTransform transform, Vector3 centre, float factor) => new(
        transform.Position + transform.Rotation.Transform(centre * (transform.Scale * (1f - factor))),
        transform.Rotation,
        transform.Scale * factor);

    private static float OriginFractionOnAxis(float min, float max) => max > min ? -min / (max - min) : float.NaN;
}
