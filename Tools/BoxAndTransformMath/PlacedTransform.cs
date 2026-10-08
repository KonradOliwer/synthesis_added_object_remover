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
