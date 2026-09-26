using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Whether a mesh is closed (watertight): every edge is shared by an even number of triangles.</summary>
internal static class MeshClosure
{
    private readonly record struct Edge(Vector3 From, Vector3 To);

    /// <summary>
    /// Corners are matched by exact position, because meshes split vertices along texture and
    /// normal seams and indices alone would make every seam look open. A mesh without any edge
    /// (only point triangles) is not closed.
    /// </summary>
    public static bool IsClosed(MeshTriangleTree tree)
    {
        var uses = new Dictionary<Edge, int>();
        for (var index = 0; index < tree.TriangleCount; index++)
        {
            var triangle = tree.GetTriangle(index);
            CountEdge(uses, triangle.A, triangle.B);
            CountEdge(uses, triangle.B, triangle.C);
            CountEdge(uses, triangle.C, triangle.A);
        }
        return uses.Count > 0 && uses.Values.All(count => count % 2 == 0);
    }

    private static void CountEdge(Dictionary<Edge, int> uses, Vector3 a, Vector3 b)
    {
        if (a == b) return;
        var edge = IsBefore(a, b) ? new Edge(a, b) : new Edge(b, a);
        uses[edge] = uses.GetValueOrDefault(edge) + 1;
    }

    /// <summary>Lexicographic X, Y, Z order, so both directions of an edge give the same key.</summary>
    private static bool IsBefore(Vector3 a, Vector3 b)
    {
        if (a.X != b.X) return a.X < b.X;
        if (a.Y != b.Y) return a.Y < b.Y;
        return a.Z < b.Z;
    }
}
