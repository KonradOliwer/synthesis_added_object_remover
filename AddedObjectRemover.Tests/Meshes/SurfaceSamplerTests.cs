using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class SurfaceSamplerTests
{
    [Fact]
    public void SamplesLieOnTheMesh()
    {
        var tree = BoxMesh.CreateTree(TestMeshes.UnitCube);
        var samples = SurfaceSampler.Sample(tree);

        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.True(IsOnUnitCubeSurface(sample), $"{sample} is off the surface."));
    }

    [Fact]
    public void NonFiniteTriangleGetsNoSamplesAndSparesTheRest()
    {
        var broken = new MeshTriangle(new Vector3(0, 0, 0), new Vector3(float.NaN, 0, 0), new Vector3(0, 1, 0));
        var triangles = TestMeshes.BoxTriangles(TestMeshes.UnitCube);
        triangles.Insert(5, broken);
        var tree = TestMeshes.Tree(triangles);

        var samples = SurfaceSampler.Sample(tree);

        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.True(IsOnUnitCubeSurface(sample), $"{sample} is off the surface."));
    }

    private static bool IsOnUnitCubeSurface(Vector3 point)
    {
        const float tolerance = 1e-5f;
        var inside = point is { X: >= -tolerance and <= 1 + tolerance, Y: >= -tolerance and <= 1 + tolerance, Z: >= -tolerance and <= 1 + tolerance };
        var onFace = MathF.Min(MathF.Min(point.X, 1 - point.X), MathF.Min(MathF.Min(point.Y, 1 - point.Y), MathF.Min(point.Z, 1 - point.Z)));
        return inside && onFace <= tolerance;
    }
}
