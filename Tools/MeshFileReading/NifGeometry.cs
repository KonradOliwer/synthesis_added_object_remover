using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Render geometry of a NIF in root-node space: its bounds plus (optionally) the triangles.</summary>
internal sealed class NifGeometry(Vector3 min, Vector3 max, MeshTriangles triangles)
{
    /// <summary>AABB of all render geometry, including bounding-sphere fallbacks of shapes without vertices.</summary>
    public Vector3 Min { get; } = min;

    public Vector3 Max { get; } = max;

    /// <summary>Empty when triangles were not requested.</summary>
    public MeshTriangles Triangles { get; } = triangles;
}
