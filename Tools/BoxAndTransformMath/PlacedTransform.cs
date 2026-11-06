using System.Numerics;

namespace AddedObjectRemover;

public readonly record struct PlacedTransform(Vector3 Position, Mat3 Rotation, float Scale)
{
    /// <summary>The transform of an object that stands upright: it turns only about Z.</summary>
    public static PlacedTransform Upright(Vector3 position, float rotationZ, float scale) =>
        new(position, Mat3.FromEuler(new Vector3(0f, 0f, rotationZ)), scale);

    /// <summary>Mesh-local -> world: position + R * (scale * local).</summary>
    public Vector3 ToWorld(Vector3 local) => Position + Rotation.Transform(local * Scale);

    /// <summary>World -> mesh-local, the inverse of <see cref="ToWorld"/>.</summary>
    public Vector3 ToLocal(Vector3 world) => Rotation.TransformTransposed(world - Position) / Scale;

    /// <summary>Whether every number of both is the same to the bit, so any computation gives the same result with either.</summary>
    public bool IsSameToTheBitAs(PlacedTransform other)
    {
        var (rotation, otherRotation) = (Rotation, other.Rotation);
        return Same(Position.X, other.Position.X) && Same(Position.Y, other.Position.Y) && Same(Position.Z, other.Position.Z) && Same(Scale, other.Scale)
            && Same(rotation.M11, otherRotation.M11) && Same(rotation.M12, otherRotation.M12) && Same(rotation.M13, otherRotation.M13)
            && Same(rotation.M21, otherRotation.M21) && Same(rotation.M22, otherRotation.M22) && Same(rotation.M23, otherRotation.M23)
            && Same(rotation.M31, otherRotation.M31) && Same(rotation.M32, otherRotation.M32) && Same(rotation.M33, otherRotation.M33);
    }

    private static bool Same(float first, float second) => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
}

/// <summary>
/// Mesh-local of one reference -> mesh-local of another: x_to = R_to^T * (pos_from + R_from * (s_from * x) - pos_to) / s_to,
/// computed as Rotation * x * Ratio + Translation.
/// </summary>
public readonly record struct RelativeTransform(Mat3 Rotation, float Ratio, Vector3 Translation)
{
    public static RelativeTransform Create(PlacedTransform from, PlacedTransform to) => new(
        to.Rotation.Transposed() * from.Rotation,
        from.Scale / to.Scale,
        to.Rotation.TransformTransposed(from.Position - to.Position) / to.Scale);

    public Vector3 Apply(Vector3 v) => Rotation.Transform(v) * Ratio + Translation;

    /// <summary>AABB enclosing the transformed box.</summary>
    public Box ApplyToBox(Box box) => Boxes.RotatedAabb(Apply(box.Center), Rotation, box.Size * (0.5f * Ratio));
}
