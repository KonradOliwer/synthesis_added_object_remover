using System.Diagnostics;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A box as a closed mesh of 12 triangles, so box shapes can go through the mesh tests.</summary>
public static class BoxMesh
{
    /// <summary>Corner i has the maximum X when bit 0 is set, Y for bit 1, Z for bit 2.</summary>
    private const int CornerCount = 8;

    private const int TriangleCount = 12;

    private static readonly MeshTreeLimits Limits = new(MaxIndexedTriangles: TriangleCount, MaxClosednessTriangles: TriangleCount);

    /// <summary>The whole box is one part, starting at triangle 0.</summary>
    private static readonly int[] SinglePart = [0];

    /// <summary>Two triangles per face: bottom, top, -Y, +Y, -X, +X.</summary>
    private static readonly int[] FaceTriangles =
    [
        0, 2, 1, 1, 2, 3,
        4, 5, 6, 5, 7, 6,
        0, 1, 4, 1, 5, 4,
        2, 6, 3, 3, 6, 7,
        0, 4, 2, 2, 4, 6,
        1, 3, 5, 3, 7, 5,
    ];

    public static MeshTriangleTree CreateTree(Box box) =>
        MeshTriangleTree.Build(CreateTriangles(box), Limits) ?? throw new UnreachableException("A box mesh always has 12 triangles.");

    public static MeshTriangles CreateTriangles(Box box)
    {
        var corners = new Vector3[CornerCount];
        for (var i = 0; i < CornerCount; i++)
        {
            corners[i] = new Vector3(
                (i & 1) != 0 ? box.Max.X : box.Min.X,
                (i & 2) != 0 ? box.Max.Y : box.Min.Y,
                (i & 4) != 0 ? box.Max.Z : box.Min.Z);
        }
        return new MeshTriangles(corners, FaceTriangles, SinglePart);
    }
}
