using System.Numerics;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Synthetic skinned-body meshes in the bind pose.</summary>
internal static class TestBodies
{
    public const float TorsoHalfWidth = 20f;
    public const float TorsoHalfDepth = 10f;
    public const float ArmSpan = 160f;
    private const float SegmentHeight = 10f;
    private const float ArmHalfThickness = 5f;
    private const float ShoulderHeightFraction = 0.8f;

    /// <summary>
    /// A torso of stacked 10-unit boxes (so there are vertices at every height) with both arms
    /// spread sideways at shoulder height, as a T-pose body mesh.
    /// </summary>
    public static List<MeshTriangle> TPose(float height, float torsoHalfWidth = TorsoHalfWidth)
    {
        var triangles = new List<MeshTriangle>();
        for (var bottom = 0f; bottom < height; bottom += SegmentHeight)
        {
            triangles.AddRange(TestMeshes.BoxTriangles(new Box(
                new Vector3(-torsoHalfWidth, -TorsoHalfDepth, bottom),
                new Vector3(torsoHalfWidth, TorsoHalfDepth, MathF.Min(bottom + SegmentHeight, height)))));
        }
        var shoulder = height * ShoulderHeightFraction;
        triangles.AddRange(TestMeshes.BoxTriangles(new Box(
            new Vector3(-ArmSpan / 2, -ArmHalfThickness, shoulder - ArmHalfThickness),
            new Vector3(ArmSpan / 2, ArmHalfThickness, shoulder + ArmHalfThickness))));
        return triangles;
    }

    public static Vector3[] Vertices(IEnumerable<MeshTriangle> triangles) =>
        triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();
}
