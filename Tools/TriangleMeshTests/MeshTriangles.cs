using System.Numerics;

namespace AddedObjectRemover;

/// <summary>The triangles of one mesh in mesh-local space, given as shared vertices plus three vertex indices per triangle.</summary>
public sealed class MeshTriangles(Vector3[] vertices, int[] indices, int[] partFirstTriangles)
{
    public Vector3[] Vertices { get; } = vertices;

    /// <summary>Three indices into <see cref="Vertices"/> per triangle. A triangle may be degenerate (a line or a point).</summary>
    public int[] Indices { get; } = indices;

    /// <summary>
    /// Per part (a separately built piece of the mesh), ascending, the index of its first
    /// triangle; a part's triangles run up to the next part's first triangle.
    /// </summary>
    public int[] PartFirstTriangles { get; } = partFirstTriangles;

    public int TriangleCount => Indices.Length / 3;

    public (Vector3 A, Vector3 B, Vector3 C) GetTriangle(int triangle) => (
        Vertices[Indices[3 * triangle]],
        Vertices[Indices[3 * triangle + 1]],
        Vertices[Indices[3 * triangle + 2]]);
}
