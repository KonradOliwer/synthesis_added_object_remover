using System.Numerics;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Hand-built meshes for the mesh tests.</summary>
internal static class TestMeshes
{
    public static readonly Box UnitCube = new(Vector3.Zero, Vector3.One);

    public static MeshTriangleTree Tree(IReadOnlyList<MeshTriangle> triangles) =>
        MeshTriangleTree.Build(Triangles(triangles, [0]), MeshLimits.Tree) ?? throw new InvalidOperationException("Mesh has no triangles.");

    /// <summary>One mesh made of several parts (NIF shapes), in the given order.</summary>
    public static MeshTriangleTree PartsTree(params IReadOnlyList<MeshTriangle>[] parts)
    {
        var partFirstTriangles = new int[parts.Length];
        for (var p = 1; p < parts.Length; p++) partFirstTriangles[p] = partFirstTriangles[p - 1] + parts[p - 1].Count;
        return MeshTriangleTree.Build(Triangles(parts.SelectMany(part => part).ToList(), partFirstTriangles), MeshLimits.Tree)
            ?? throw new InvalidOperationException("Mesh has no triangles.");
    }

    private static MeshTriangles Triangles(IReadOnlyList<MeshTriangle> triangles, int[] partFirstTriangles)
    {
        var vertices = new Vector3[triangles.Count * 3];
        var indices = new int[triangles.Count * 3];
        for (var t = 0; t < triangles.Count; t++)
        {
            vertices[3 * t] = triangles[t].A;
            vertices[3 * t + 1] = triangles[t].B;
            vertices[3 * t + 2] = triangles[t].C;
            indices[3 * t] = 3 * t;
            indices[3 * t + 1] = 3 * t + 1;
            indices[3 * t + 2] = 3 * t + 2;
        }
        return new MeshTriangles(vertices, indices, partFirstTriangles);
    }

    /// <summary>The 12 triangles of <see cref="BoxMesh"/> for <paramref name="box"/>.</summary>
    public static List<MeshTriangle> BoxTriangles(Box box)
    {
        var geometry = BoxMesh.CreateTriangles(box);
        return Enumerable.Range(0, geometry.TriangleCount)
            .Select(t => geometry.GetTriangle(t))
            .Select(corners => new MeshTriangle(corners.A, corners.B, corners.C))
            .ToList();
    }

    /// <summary>The box mesh without the triangles whose corners all lie on the given face.</summary>
    public static List<MeshTriangle> BoxWithoutFace(Box box, Func<Vector3, bool> isOnFace) =>
        BoxTriangles(box).Where(t => !(isOnFace(t.A) && isOnFace(t.B) && isOnFace(t.C))).ToList();

    public static MeshTriangle RandomTriangle(Random random, float extent, float size)
    {
        var anchor = RandomVector(random, extent);
        return new MeshTriangle(anchor, anchor + RandomVector(random, size), anchor + RandomVector(random, size));
    }

    public static float RandomAngle(Random random) => (float)((random.NextDouble() * 2 - 1) * Math.PI);

    public static Vector3 RandomVector(Random random, float extent) => new(
        (float)((random.NextDouble() * 2 - 1) * extent),
        (float)((random.NextDouble() * 2 - 1) * extent),
        (float)((random.NextDouble() * 2 - 1) * extent));
}
