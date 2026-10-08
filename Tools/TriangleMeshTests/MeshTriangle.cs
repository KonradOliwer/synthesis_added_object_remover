using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A triangle given by its three corners.</summary>
public readonly record struct MeshTriangle(Vector3 A, Vector3 B, Vector3 C)
{
    public MeshTriangle Transformed(RelativeTransform transform) => new(transform.Apply(A), transform.Apply(B), transform.Apply(C));

    public Box Bounds => new(Vector3.Min(A, Vector3.Min(B, C)), Vector3.Max(A, Vector3.Max(B, C)));
}
