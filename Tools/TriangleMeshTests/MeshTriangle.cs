using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A triangle given by its three corners.</summary>
public readonly record struct MeshTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public MeshTriangle Transformed(RelativeTransform transform) => new(transform.Apply(A), transform.Apply(B), transform.Apply(C));

    public Box Bounds => new(Vector3.Min(A, Vector3.Min(B, C)), Vector3.Max(A, Vector3.Max(B, C)));

    /// <summary>The side the corners turn counter-clockwise around, not normalized; zero for a degenerate triangle.</summary>
    public Vector3 Normal => Vector3.Cross(B - A, C - A);
}
