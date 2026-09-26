using System.Numerics;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A winning navmesh and the exterior cell holding it.</summary>
/// <param name="Grid">Null for a navmesh of an interior cell or of a worldspace's persistent cell.</param>
internal readonly record struct CellNavmesh((int X, int Y)? Grid, INavigationMeshDataGetter Data);

internal static class NavmeshTriangles
{
    /// <summary>Triangles (world space) of one navmesh record; triangles naming a missing vertex (broken data) are skipped.</summary>
    public static IEnumerable<MeshTriangle> Read(INavigationMeshDataGetter data)
    {
        var vertices = data.Vertices.Select(Geometry.ToVector).ToArray();
        foreach (var triangle in data.Triangles)
        {
            var corners = triangle.Vertices;
            if (!IsVertex(corners.X, vertices) || !IsVertex(corners.Y, vertices) || !IsVertex(corners.Z, vertices)) continue;
            yield return new MeshTriangle(vertices[corners.X], vertices[corners.Y], vertices[corners.Z]);
        }
    }

    private static bool IsVertex(short index, Vector3[] vertices) => index >= 0 && index < vertices.Length;
}
