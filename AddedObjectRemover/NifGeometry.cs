using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Render geometry of a NIF in root-node space: its bounds plus (optionally) the triangles.</summary>
internal sealed class NifGeometry(Vector3 min, Vector3 max, Vector3[] vertices, int[] indices, int[] partFirstTriangles)
{
    /// <summary>AABB of all render geometry, including bounding-sphere fallbacks of shapes without vertices.</summary>
    public Vector3 Min { get; } = min;

    public Vector3 Max { get; } = max;

    /// <summary>Empty when triangles were not requested.</summary>
    public Vector3[] Vertices { get; } = vertices;

    /// <summary>
    /// Three indices into <see cref="Vertices"/> per triangle. Shapes that have vertices but no
    /// triangle list contribute each vertex as a degenerate (point) triangle.
    /// </summary>
    public int[] Indices { get; } = indices;

    /// <summary>
    /// Per part (one NIF shape that contributed triangles), ascending, the index of its first
    /// triangle; a part's triangles run up to the next part's first triangle.
    /// </summary>
    public int[] PartFirstTriangles { get; } = partFirstTriangles;

    public int TriangleCount => Indices.Length / 3;

    public (Vector3 A, Vector3 B, Vector3 C) GetTriangle(int triangle) => (
        Vertices[Indices[3 * triangle]],
        Vertices[Indices[3 * triangle + 1]],
        Vertices[Indices[3 * triangle + 2]]);
}
