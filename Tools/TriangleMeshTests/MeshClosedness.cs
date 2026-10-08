using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Whether a mesh is closed: every edge is shared by an even number of triangles, so nothing can
/// leak out through a hole. Edges are compared by their end positions, not vertex indices, because
/// meshes duplicate vertices along texture seams and between parts.
/// </summary>
public static class MeshClosedness
{
    private const int SharedEdgeParity = 2;

    /// <summary>A mesh with more than <paramref name="maxTriangles"/> triangles counts as open.</summary>
    public static bool IsClosed(MeshTriangleTree tree, int maxTriangles)
    {
        if (tree.TriangleCount > maxTriangles) return false;
        var edgeUses = CountEdgeUses(tree);
        return edgeUses.Count > 0 && edgeUses.Values.All(uses => uses % SharedEdgeParity == 0);
    }

    /// <summary>Zero-length edges (point and degenerate triangles) are skipped.</summary>
    private static Dictionary<(Vector3, Vector3), int> CountEdgeUses(MeshTriangleTree tree)
    {
        var edgeUses = new Dictionary<(Vector3, Vector3), int>();
        for (var t = 0; t < tree.TriangleCount; t++)
        {
            var triangle = tree.GetTriangle(t);
            AddEdge(edgeUses, triangle.A, triangle.B);
            AddEdge(edgeUses, triangle.B, triangle.C);
            AddEdge(edgeUses, triangle.C, triangle.A);
        }
        return edgeUses;
    }

    private static void AddEdge(Dictionary<(Vector3, Vector3), int> edgeUses, Vector3 start, Vector3 end)
    {
        if (start == end) return;
        var key = IsBefore(start, end) ? (start, end) : (end, start);
        edgeUses[key] = edgeUses.GetValueOrDefault(key) + 1;
    }

    /// <summary>Lexicographic order, so both directions of an edge give the same key.</summary>
    private static bool IsBefore(Vector3 a, Vector3 b)
    {
        if (a.X != b.X) return a.X < b.X;
        if (a.Y != b.Y) return a.Y < b.Y;
        return a.Z < b.Z;
    }
}
