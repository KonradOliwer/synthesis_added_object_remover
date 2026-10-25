using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class MeshTouchTests
{
    private static readonly MeshTriangleTree Large = BoxMesh.CreateTree(new Box(new Vector3(-1), new Vector3(1)));

    /// <summary>A half-size cube at scale 2, turned a quarter, whose -X face is 0.5 from the large cube's +X face.</summary>
    private static readonly MeshTriangleTree Small = TestMeshes.Tree(TestMeshes.BoxTriangles(new Box(new Vector3(-0.5f), new Vector3(0.5f))).Concat(
        TestMeshes.BoxTriangles(new Box(new Vector3(-0.1f), new Vector3(0.1f)))).ToList());

    private static readonly PlacedTransform LargeAt = TestTargets.At(Vector3.Zero);
    private static readonly PlacedTransform SmallAt = TestTargets.At(new Vector3(2.5f, 0, 0), zRadians: MathF.PI / 2, scale: 2);

    [Theory]
    [InlineData(0.6f, true)]
    [InlineData(0.4f, false)]
    public void TouchUsesWorldTolerance(float tolerance, bool expected)
    {
        Assert.Equal(expected, MeshContact.SurfacesTouch(Large, LargeAt, Small, SmallAt, tolerance, new TouchScratch()));
        Assert.Equal(expected, MeshContact.SurfacesTouch(Small, SmallAt, Large, LargeAt, tolerance, new TouchScratch()));
    }

    /// <summary>Every triangle of either mesh touches every triangle of the other, so the first pair tested touches.</summary>
    [Fact]
    public void TouchStopsAtTheFirstTouchingPair()
    {
        var stacked = TestMeshes.Tree([.. Enumerable.Range(0, 10).Select(i => FlatTriangle(z: i * 0.01f))]);
        var pair = TestMeshes.Tree([FlatTriangle(z: 0f), FlatTriangle(z: 0.05f)]);
        var scratch = new TouchScratch();

        Assert.True(MeshContact.SurfacesTouch(stacked, LargeAt, pair, LargeAt, 0.5f, scratch));
        Assert.Equal(1, scratch.TrianglePairsTested);
    }

    [Fact]
    public void MinSurfaceDistanceIsTheGapEitherWay()
    {
        Assert.Equal(0.5f, MeshContact.MinSurfaceDistance(Large, LargeAt, Small, SmallAt, 0.6f, new TouchScratch()), 1e-4f);
        Assert.Equal(0.5f, MeshContact.MinSurfaceDistance(Small, SmallAt, Large, LargeAt, 0.6f, new TouchScratch()), 1e-4f);
    }

    private static MeshTriangle FlatTriangle(float z) => new(new Vector3(0, 0, z), new Vector3(1, 0, z), new Vector3(0, 1, z));
}
