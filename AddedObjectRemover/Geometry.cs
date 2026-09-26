using System.Numerics;
using Noggog;

namespace AddedObjectRemover;

/// <summary>Axis-aligned box given by its minimum and maximum corners.</summary>
internal readonly record struct Box(Vector3 Min, Vector3 Max)
{
    public static Box Zero => new(Vector3.Zero, Vector3.Zero);

    public Vector3 Center => (Min + Max) * 0.5f;

    public Vector3 Size => Max - Min;

    /// <summary>Builds a box from two arbitrary corners (order per axis does not matter).</summary>
    public static Box FromCorners(Vector3 a, Vector3 b) => new(Vector3.Min(a, b), Vector3.Max(a, b));

    /// <summary>Both corners multiplied by <paramref name="scale"/>, re-ordered (a negative scale flips them).</summary>
    public Box Scaled(float scale) => FromCorners(Min * scale, Max * scale);

    /// <summary>Inclusive containment test.</summary>
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X
        && p.Y >= Min.Y && p.Y <= Max.Y
        && p.Z >= Min.Z && p.Z <= Max.Z;
}

/// <summary>Row-major 3x3 matrix applied to column vectors (v' = M * v).</summary>
internal readonly struct Mat3
{
    public readonly float M11, M12, M13;
    public readonly float M21, M22, M23;
    public readonly float M31, M32, M33;

    public Mat3(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        M11 = m11; M12 = m12; M13 = m13;
        M21 = m21; M22 = m22; M23 = m23;
        M31 = m31; M32 = m32; M33 = m33;
    }

    public static Mat3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Mat3 operator *(Mat3 a, Mat3 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33);

    /// <summary>M^T (the inverse, for a rotation matrix).</summary>
    public Mat3 Transposed() => new(
        M11, M21, M31,
        M12, M22, M32,
        M13, M23, M33);

    /// <summary>M * v (local -> world for a rotation matrix).</summary>
    public Vector3 Transform(Vector3 v) => new(
        M11 * v.X + M12 * v.Y + M13 * v.Z,
        M21 * v.X + M22 * v.Y + M23 * v.Z,
        M31 * v.X + M32 * v.Y + M33 * v.Z);

    /// <summary>M^T * v (world -> local for a rotation matrix, since R^-1 = R^T).</summary>
    public Vector3 TransformTransposed(Vector3 v) => new(
        M11 * v.X + M21 * v.Y + M31 * v.Z,
        M12 * v.X + M22 * v.Y + M32 * v.Z,
        M13 * v.X + M23 * v.Y + M33 * v.Z);

    /// <summary>|M| * v with element-wise absolute values; used to get world half-extents of a rotated box.</summary>
    public Vector3 AbsTransform(Vector3 v) => new(
        MathF.Abs(M11) * v.X + MathF.Abs(M12) * v.Y + MathF.Abs(M13) * v.Z,
        MathF.Abs(M21) * v.X + MathF.Abs(M22) * v.Y + MathF.Abs(M23) * v.Z,
        MathF.Abs(M31) * v.X + MathF.Abs(M32) * v.Y + MathF.Abs(M33) * v.Z);
}

internal readonly record struct PlacedTransform(Vector3 Position, Mat3 Rotation, float Scale);

/// <summary>
/// Oriented box in world space: world = Center + Rotation * local, with local in
/// [-HalfExtents, +HalfExtents]. The columns of <see cref="Rotation"/> are the box axes.
/// </summary>
internal readonly record struct OrientedBox(Vector3 Center, Mat3 Rotation, Vector3 HalfExtents)
{
    /// <summary>Added to every |R| term so near-parallel axes do not produce a false separation from rounding.</summary>
    private const float ParallelAxisEpsilon = 1e-5f;

    public static OrientedBox FromLocal(Box local, PlacedTransform transform)
    {
        var scaled = local.Scaled(transform.Scale);
        return new OrientedBox(
            transform.Position + transform.Rotation.Transform(scaled.Center),
            transform.Rotation,
            scaled.Size * 0.5f);
    }

    /// <summary>World AABB enclosing this box grown by <paramref name="padding"/> on every side.</summary>
    public Box WorldAabb(float padding)
    {
        var half = Rotation.AbsTransform(HalfExtents) + new Vector3(padding);
        return new Box(Center - half, Center + half);
    }

    /// <summary>
    /// Separating axis test (15 axes, Ericson, Real-Time Collision Detection 4.4.1): true if the
    /// boxes intersect once this box is grown by <paramref name="padding"/> on every side
    /// (padding is a conservative stand-in for "within distance padding" of each other).
    /// </summary>
    public bool Intersects(OrientedBox other, float padding)
    {
        var a = HalfExtents + new Vector3(padding);
        var b = other.HalfExtents;

        // r = A^T * B: other's axes expressed in this box's frame. t = translation in this frame.
        var r = Rotation.Transposed() * other.Rotation;
        var t = Rotation.TransformTransposed(other.Center - Center);

        float r00 = r.M11, r01 = r.M12, r02 = r.M13;
        float r10 = r.M21, r11 = r.M22, r12 = r.M23;
        float r20 = r.M31, r21 = r.M32, r22 = r.M33;
        float a00 = MathF.Abs(r00) + ParallelAxisEpsilon, a01 = MathF.Abs(r01) + ParallelAxisEpsilon, a02 = MathF.Abs(r02) + ParallelAxisEpsilon;
        float a10 = MathF.Abs(r10) + ParallelAxisEpsilon, a11 = MathF.Abs(r11) + ParallelAxisEpsilon, a12 = MathF.Abs(r12) + ParallelAxisEpsilon;
        float a20 = MathF.Abs(r20) + ParallelAxisEpsilon, a21 = MathF.Abs(r21) + ParallelAxisEpsilon, a22 = MathF.Abs(r22) + ParallelAxisEpsilon;

        // This box's axes.
        if (MathF.Abs(t.X) > a.X + b.X * a00 + b.Y * a01 + b.Z * a02) return false;
        if (MathF.Abs(t.Y) > a.Y + b.X * a10 + b.Y * a11 + b.Z * a12) return false;
        if (MathF.Abs(t.Z) > a.Z + b.X * a20 + b.Y * a21 + b.Z * a22) return false;

        // Other box's axes.
        if (MathF.Abs(t.X * r00 + t.Y * r10 + t.Z * r20) > a.X * a00 + a.Y * a10 + a.Z * a20 + b.X) return false;
        if (MathF.Abs(t.X * r01 + t.Y * r11 + t.Z * r21) > a.X * a01 + a.Y * a11 + a.Z * a21 + b.Y) return false;
        if (MathF.Abs(t.X * r02 + t.Y * r12 + t.Z * r22) > a.X * a02 + a.Y * a12 + a.Z * a22 + b.Z) return false;

        // Cross products A_i x B_j.
        if (MathF.Abs(t.Z * r10 - t.Y * r20) > a.Y * a20 + a.Z * a10 + b.Y * a02 + b.Z * a01) return false;
        if (MathF.Abs(t.Z * r11 - t.Y * r21) > a.Y * a21 + a.Z * a11 + b.X * a02 + b.Z * a00) return false;
        if (MathF.Abs(t.Z * r12 - t.Y * r22) > a.Y * a22 + a.Z * a12 + b.X * a01 + b.Y * a00) return false;
        if (MathF.Abs(t.X * r20 - t.Z * r00) > a.X * a20 + a.Z * a00 + b.Y * a12 + b.Z * a11) return false;
        if (MathF.Abs(t.X * r21 - t.Z * r01) > a.X * a21 + a.Z * a01 + b.X * a12 + b.Z * a10) return false;
        if (MathF.Abs(t.X * r22 - t.Z * r02) > a.X * a22 + a.Z * a02 + b.X * a11 + b.Y * a10) return false;
        if (MathF.Abs(t.Y * r00 - t.X * r10) > a.X * a10 + a.Y * a00 + b.Y * a22 + b.Z * a21) return false;
        if (MathF.Abs(t.Y * r01 - t.X * r11) > a.X * a11 + a.Y * a01 + b.X * a22 + b.Z * a20) return false;
        if (MathF.Abs(t.Y * r02 - t.X * r12) > a.X * a12 + a.Y * a02 + b.X * a21 + b.Y * a20) return false;

        return true;
    }
}

internal static class Geometry
{
    /// <summary>
    /// Largest accepted absolute mesh or placement coordinate. Real content stays far below it
    /// (a worldspace spans a few hundred thousand units); larger values come from broken exports
    /// or sentinel values and would blow up grids and voxelization.
    /// </summary>
    public const float MaxCoordinate = 1e6f;

    public static Vector3 ToVector(P3Float p) => new(p.X, p.Y, p.Z);

    /// <summary>A placed reference's scale; missing, non-finite or non-positive values mean 1.</summary>
    public static float NormalizeScale(float? scale) =>
        scale is { } s && float.IsFinite(s) && s > 0 ? s : 1f;

    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>Finite and every component within ±<see cref="MaxCoordinate"/>.</summary>
    public static bool IsWithinLimits(Vector3 v) =>
        MathF.Abs(v.X) <= MaxCoordinate && MathF.Abs(v.Y) <= MaxCoordinate && MathF.Abs(v.Z) <= MaxCoordinate;

    // TODO: verify the multi-axis order in-game.
    /// <summary>
    /// Turns a placed reference's Euler rotation (REFR/ACHR DATA, radians, X/Y/Z as stored in
    /// Placement.Rotation) into a rotation matrix.
    ///
    /// Convention: the Creation Engine rotates clockwise (left-handed) about each axis, which is the
    /// same as a standard right-handed rotation by the negated angle. The composite world matrix is
    ///     R = Rx(-x) * Ry(-y) * Rz(-z)
    /// built from the standard right-handed matrices
    ///     Rx(a) = [1 0 0; 0 cos a -sin a; 0 sin a cos a]
    ///     Ry(a) = [cos a 0 sin a; 0 1 0; -sin a 0 cos a]
    ///     Rz(a) = [cos a -sin a 0; sin a cos a 0; 0 0 1]
    /// and applied to column vectors: v_world = R * v_local (+ position).
    /// </summary>
    public static Mat3 RotationFromEuler(P3Float rotationRadians)
    {
        var rx = RotationX(-rotationRadians.X);
        var ry = RotationY(-rotationRadians.Y);
        var rz = RotationZ(-rotationRadians.Z);
        return rx * ry * rz;
    }

    private static Mat3 RotationX(float a)
    {
        var c = MathF.Cos(a);
        var s = MathF.Sin(a);
        return new Mat3(
            1, 0, 0,
            0, c, -s,
            0, s, c);
    }

    private static Mat3 RotationY(float a)
    {
        var c = MathF.Cos(a);
        var s = MathF.Sin(a);
        return new Mat3(
            c, 0, s,
            0, 1, 0,
            -s, 0, c);
    }

    private static Mat3 RotationZ(float a)
    {
        var c = MathF.Cos(a);
        var s = MathF.Sin(a);
        return new Mat3(
            c, -s, 0,
            s, c, 0,
            0, 0, 1);
    }

    /// <summary>
    /// Scales a local box by the reference scale and grows it on every side by
    /// <paramref name="multiplier"/> times its (scaled) size along that axis.
    /// </summary>
    public static Box ExpandedLocalBox(Box local, float scale, float multiplier)
    {
        var scaled = local.Scaled(scale);
        var padding = scaled.Size * multiplier;
        return new Box(scaled.Min - padding, scaled.Max + padding);
    }

    public static Box WorldAabb(Box local, Vector3 position, Mat3 rotation)
    {
        var center = position + rotation.Transform(local.Center);
        var halfExtents = rotation.AbsTransform(local.Size * 0.5f);
        return new Box(center - halfExtents, center + halfExtents);
    }

    public static Vector3 WorldBoundsCenter(Box local, PlacedTransform transform) =>
        transform.Position + transform.Rotation.Transform(local.Center * transform.Scale);

    /// <summary>Inclusive test in the reference's local frame: p_local = R^T * (p - position).</summary>
    public static bool IsInsideOrientedBox(Vector3 worldPoint, Vector3 position, Mat3 rotation, Box localBox) =>
        localBox.Contains(rotation.TransformTransposed(worldPoint - position));

    /// <summary>Squared distance from a point to an axis-aligned box (0 inside).</summary>
    public static float DistanceSquaredToBox(Vector3 p, Vector3 min, Vector3 max)
    {
        var d = Vector3.Max(Vector3.Max(min - p, p - max), Vector3.Zero);
        return d.LengthSquared();
    }

    /// <summary>
    /// Squared distance from <paramref name="p"/> to triangle (a, b, c), via the closest point on
    /// the triangle (Ericson, Real-Time Collision Detection 5.1.5). Degenerate triangles
    /// (coincident vertices, collinear vertices, a single point) are handled without dividing by
    /// zero: an edge of zero length is treated as its start point, and a zero-area triangle as its
    /// three edges.
    /// </summary>
    public static float DistanceSquaredToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var abDenominator = d1 - d3;
            var v = abDenominator != 0 ? d1 / abDenominator : 0f;
            return (p - (a + v * ab)).LengthSquared();
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var acDenominator = d2 - d6;
            var w = acDenominator != 0 ? d2 / acDenominator : 0f;
            return (p - (a + w * ac)).LengthSquared();
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var bcDenominator = (d4 - d3) + (d5 - d6);
            var w = bcDenominator != 0 ? (d4 - d3) / bcDenominator : 0f;
            return (p - (b + w * (c - b))).LengthSquared();
        }

        var sum = va + vb + vc;
        if (!float.IsFinite(1f / sum))
        {
            return MathF.Min(
                DistanceSquaredToSegment(p, a, b),
                MathF.Min(DistanceSquaredToSegment(p, b, c), DistanceSquaredToSegment(p, c, a)));
        }
        var denominator = 1f / sum;
        var vv = vb * denominator;
        var ww = vc * denominator;
        return (p - (a + ab * vv + ac * ww)).LengthSquared();
    }

    private static float DistanceSquaredToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(Vector3.Dot(p - a, ab) / lengthSquared, 0f, 1f) : 0f;
        return (p - (a + t * ab)).LengthSquared();
    }
}
