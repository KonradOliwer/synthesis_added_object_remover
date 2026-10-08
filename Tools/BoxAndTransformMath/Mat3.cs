using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Row-major 3x3 matrix applied to column vectors (v' = M * v).</summary>
public readonly struct Mat3
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

    /// <summary>
    /// Turns a placed reference's Euler rotation (radians, X/Y/Z as stored in the placement) into a
    /// rotation matrix.
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
    public static Mat3 FromEuler(Vector3 rotationRadians)
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
