using System.Numerics;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

internal static class NavmeshTriangles
{
    /// <summary>Triangles (world space) of one navmesh record; triangles naming a missing vertex (broken data) are skipped.</summary>
    public static IEnumerable<MeshTriangle> Read(INavigationMeshDataGetter data)
    {
        var vertices = data.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();
        foreach (var triangle in data.Triangles)
        {
            var corners = triangle.Vertices;
            if (!IsVertex(corners.X, vertices) || !IsVertex(corners.Y, vertices) || !IsVertex(corners.Z, vertices)) continue;
            yield return new MeshTriangle(vertices[corners.X], vertices[corners.Y], vertices[corners.Z]);
        }
    }

    private static bool IsVertex(short index, Vector3[] vertices) => index >= 0 && index < vertices.Length;
}
