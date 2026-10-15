using System.Numerics;
using NiflySharp.Structs;

namespace AddedObjectRemover;

/// <summary>
/// Similarity transform p' = T + S * (R * p), matching nifly's documented MatTransform
/// semantics. R rows are taken from Matrix33 M11..M33 exactly as NiflySharp's own
/// NiNode.TransformToParent builds them.
/// </summary>
internal readonly record struct Similarity(Mat3 Rotation, float Scale, Vector3 Translation)
{
    public static Similarity Identity => new(Mat3.Identity, 1f, Vector3.Zero);

    public static Similarity From(Vector3 translation, Matrix33 rotation, float scale) => new(
        new Mat3(
            rotation.M11, rotation.M12, rotation.M13,
            rotation.M21, rotation.M22, rotation.M23,
            rotation.M31, rotation.M32, rotation.M33),
        scale,
        translation);

    public Vector3 Apply(Vector3 v) => Translation + Rotation.Transform(v * Scale);

    /// <summary>
    /// this(child(v)) = T + S*R*childT + (S*childS) * (R*childR) * v.
    /// </summary>
    public Similarity After(Similarity child) => new(
        Rotation * child.Rotation,
        Scale * child.Scale,
        Translation + Rotation.Transform(child.Translation * Scale));
}
