using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Oriented box in world space: world = Center + Rotation * local, with local in
/// [-HalfExtents, +HalfExtents]. The columns of <see cref="Rotation"/> are the box axes.
/// </summary>
public readonly record struct OrientedBox(Vector3 Center, Mat3 Rotation, Vector3 HalfExtents)
{
    /// <summary>Added to every |R| term so near-parallel axes do not produce a false separation from rounding.</summary>
    private const float ParallelAxisEpsilon = 1e-5f;

    private static readonly float[] CornerSigns = [-1f, 1f];

    public static OrientedBox FromLocal(Box local, PlacedTransform transform)
    {
        var scaled = local.Scaled(transform.Scale);
        return new OrientedBox(
            transform.Position + transform.Rotation.Transform(scaled.Center),
            transform.Rotation,
            scaled.HalfExtents);
    }

    /// <summary>
    /// Ground area: the area of the box projected straight down onto the world XY plane. Each
    /// face pair contributes its area times the vertical component of its normal (row 3 of the rotation).
    /// </summary>
    public float GroundArea =>
        4f * (HalfExtents.Y * HalfExtents.Z * MathF.Abs(Rotation.M31)
              + HalfExtents.X * HalfExtents.Z * MathF.Abs(Rotation.M32)
              + HalfExtents.X * HalfExtents.Y * MathF.Abs(Rotation.M33));

    public IEnumerable<Vector3> Corners()
    {
        var center = Center;
        var rotation = Rotation;
        var halfExtents = HalfExtents;
        return
            from x in CornerSigns
            from y in CornerSigns
            from z in CornerSigns
            select center + rotation.Transform(halfExtents * new Vector3(x, y, z));
    }

    /// <summary>Inclusive test: whether the world point lies in the box.</summary>
    public bool Contains(Vector3 point)
    {
        var local = Vector3.Abs(Rotation.TransformTransposed(point - Center));
        return local.X <= HalfExtents.X && local.Y <= HalfExtents.Y && local.Z <= HalfExtents.Z;
    }

    /// <summary>
    /// Inclusive test: whether <paramref name="inner"/> lies entirely in this box, i.e. its extent
    /// along each of this box's axes stays within this box's half extent. A NaN makes it fail.
    /// </summary>
    public bool Contains(OrientedBox inner)
    {
        var offset = Vector3.Abs(Rotation.TransformTransposed(inner.Center - Center));
        var reach = offset + (Rotation.Transposed() * inner.Rotation).AbsTransform(inner.HalfExtents);
        return reach.X <= HalfExtents.X && reach.Y <= HalfExtents.Y && reach.Z <= HalfExtents.Z;
    }

    /// <summary>
    /// Inclusive test: whether the vertical line through <paramref name="point"/> passes through the
    /// box, i.e. the point lies inside the box or straight above or below it.
    /// </summary>
    public bool IsCrossedByVerticalLine(Vector3 point)
    {
        var origin = Rotation.TransformTransposed(point - Center);
        // World up (0, 0, 1) in the box frame: R^T * up is the rotation's third row.
        var up = new Vector3(Rotation.M31, Rotation.M32, Rotation.M33);
        var entry = float.NegativeInfinity;
        var exit = float.PositiveInfinity;
        return NarrowToSlab(origin.X, up.X, HalfExtents.X, ref entry, ref exit)
               && NarrowToSlab(origin.Y, up.Y, HalfExtents.Y, ref entry, ref exit)
               && NarrowToSlab(origin.Z, up.Z, HalfExtents.Z, ref entry, ref exit)
               && entry <= exit;
    }

    /// <summary>
    /// Narrows the line's parameter range to the part inside one axis slab; false when a line
    /// parallel to the slab lies outside it.
    /// </summary>
    private static bool NarrowToSlab(float origin, float direction, float halfExtent, ref float entry, ref float exit)
    {
        if (MathF.Abs(direction) <= ParallelAxisEpsilon) return MathF.Abs(origin) <= halfExtent;
        var first = (-halfExtent - origin) / direction;
        var second = (halfExtent - origin) / direction;
        entry = MathF.Max(entry, MathF.Min(first, second));
        exit = MathF.Min(exit, MathF.Max(first, second));
        return true;
    }

    /// <summary>World AABB enclosing this box grown by <paramref name="padding"/> on every side.</summary>
    public Box WorldAabb(float padding) => Boxes.RotatedAabb(Center, Rotation, HalfExtents).Grown(padding);

    /// <summary>The point of this box (surface or inside) closest to <paramref name="point"/>; the point itself when inside.</summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        var local = Rotation.TransformTransposed(point - Center);
        return Center + Rotation.Transform(Vector3.Clamp(local, -HalfExtents, HalfExtents));
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
        if (Separates(t.X, a.X + b.X * a00 + b.Y * a01 + b.Z * a02)) return false;
        if (Separates(t.Y, a.Y + b.X * a10 + b.Y * a11 + b.Z * a12)) return false;
        if (Separates(t.Z, a.Z + b.X * a20 + b.Y * a21 + b.Z * a22)) return false;

        // Other box's axes.
        if (Separates(t.X * r00 + t.Y * r10 + t.Z * r20, a.X * a00 + a.Y * a10 + a.Z * a20 + b.X)) return false;
        if (Separates(t.X * r01 + t.Y * r11 + t.Z * r21, a.X * a01 + a.Y * a11 + a.Z * a21 + b.Y)) return false;
        if (Separates(t.X * r02 + t.Y * r12 + t.Z * r22, a.X * a02 + a.Y * a12 + a.Z * a22 + b.Z)) return false;

        // Cross products A_i x B_j.
        if (Separates(t.Z * r10 - t.Y * r20, a.Y * a20 + a.Z * a10 + b.Y * a02 + b.Z * a01)) return false;
        if (Separates(t.Z * r11 - t.Y * r21, a.Y * a21 + a.Z * a11 + b.X * a02 + b.Z * a00)) return false;
        if (Separates(t.Z * r12 - t.Y * r22, a.Y * a22 + a.Z * a12 + b.X * a01 + b.Y * a00)) return false;
        if (Separates(t.X * r20 - t.Z * r00, a.X * a20 + a.Z * a00 + b.Y * a12 + b.Z * a11)) return false;
        if (Separates(t.X * r21 - t.Z * r01, a.X * a21 + a.Z * a01 + b.X * a12 + b.Z * a10)) return false;
        if (Separates(t.X * r22 - t.Z * r02, a.X * a22 + a.Z * a02 + b.X * a11 + b.Y * a10)) return false;
        if (Separates(t.Y * r00 - t.X * r10, a.X * a10 + a.Y * a00 + b.Y * a22 + b.Z * a21)) return false;
        if (Separates(t.Y * r01 - t.X * r11, a.X * a11 + a.Y * a01 + b.X * a22 + b.Z * a20)) return false;
        if (Separates(t.Y * r02 - t.X * r12, a.X * a12 + a.Y * a02 + b.X * a21 + b.Y * a20)) return false;

        return true;
    }

    /// <remarks>Written as "not within" so that a NaN projection or bound separates: a broken box intersects nothing.</remarks>
    private static bool Separates(float projection, float bound) => !(MathF.Abs(projection) <= bound);
}
