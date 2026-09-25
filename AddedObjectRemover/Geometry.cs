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

    /// <summary>Inclusive containment test.</summary>
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X
        && p.Y >= Min.Y && p.Y <= Max.Y
        && p.Z >= Min.Z && p.Z <= Max.Z;
}

/// <summary>
/// Row-major 3x3 matrix applied to column vectors (v' = M * v).
/// Kept deliberately small and explicit so the rotation convention is easy to audit.
/// </summary>
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

/// <summary>Position, rotation and uniform scale of a placed reference.</summary>
internal readonly record struct PlacedTransform(Vector3 Position, Mat3 Rotation, float Scale);

internal static class Geometry
{
    public static Vector3 ToVector(P3Float p) => new(p.X, p.Y, p.Z);

    public static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// THE single place where a placed reference's Euler rotation (REFR/ACHR DATA, radians,
    /// X/Y/Z as stored in Placement.Rotation) is turned into a rotation matrix.
    ///
    /// Convention: the Creation Engine rotates clockwise (left-handed) about each axis, which is the
    /// same as a standard right-handed rotation by the negated angle. The composite world matrix is
    ///     R = Rx(-x) * Ry(-y) * Rz(-z)
    /// built from the standard right-handed matrices
    ///     Rx(a) = [1 0 0; 0 cos a -sin a; 0 sin a cos a]
    ///     Ry(a) = [cos a 0 sin a; 0 1 0; -sin a 0 cos a]
    ///     Rz(a) = [cos a -sin a 0; sin a cos a 0; 0 0 1]
    /// and applied to column vectors: v_world = R * v_local (+ position).
    ///
    /// NOTE: this convention should be verified in-game (e.g. place a long, thin object rotated on
    /// all three axes and compare its extents with what this matrix predicts). If it turns out to
    /// be wrong, only this function needs to change.
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
        var scaled = Box.FromCorners(local.Min * scale, local.Max * scale);
        var padding = scaled.Size * multiplier;
        return new Box(scaled.Min - padding, scaled.Max + padding);
    }

    /// <summary>World-space AABB enclosing a local box after rotation and translation.</summary>
    public static Box WorldAabb(Box local, Vector3 position, Mat3 rotation)
    {
        var center = position + rotation.Transform(local.Center);
        var halfExtents = rotation.AbsTransform(local.Size * 0.5f);
        return new Box(center - halfExtents, center + halfExtents);
    }

    /// <summary>World-space center of a reference's (scaled, rotated) local bounds.</summary>
    public static Vector3 WorldBoundsCenter(Box local, PlacedTransform transform) =>
        transform.Position + transform.Rotation.Transform(local.Center * transform.Scale);

    /// <summary>
    /// True if <paramref name="worldPoint"/> lies inside <paramref name="localBox"/> (inclusive) once the
    /// point is transformed into the reference's local frame: p_local = R^T * (p - position).
    /// </summary>
    public static bool IsInsideOrientedBox(Vector3 worldPoint, Vector3 position, Mat3 rotation, Box localBox) =>
        localBox.Contains(rotation.TransformTransposed(worldPoint - position));
}
