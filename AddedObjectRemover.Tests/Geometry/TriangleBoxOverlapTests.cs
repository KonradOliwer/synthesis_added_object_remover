using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Geometry;

public class TriangleBoxOverlapTests
{
    private static readonly Box Cube = new(new Vector3(-1), new Vector3(1));

    [Fact]
    public void TriangleInsideTheBoxOverlapsIt()
    {
        var triangle = new MeshTriangle(new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 0.5f, 0));

        Assert.True(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void LargeTriangleCrossingTheBoxOverlapsIt()
    {
        var triangle = new MeshTriangle(new Vector3(-100, -100, 0), new Vector3(100, -100, 0), new Vector3(0, 100, 0));

        Assert.True(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void TrianglePassingDiagonallyBesideTheBoxDoesNotOverlapIt()
    {
        // Its bounds overlap the cube, but the triangle's plane passes the corner (1, 1, 1).
        var triangle = new MeshTriangle(new Vector3(4, 0, 0), new Vector3(0, 4, 0), new Vector3(0, 0, 4));

        Assert.True(triangle.Bounds.Overlaps(Cube));
        Assert.False(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void TriangleTouchingACornerOverlaps()
    {
        var triangle = new MeshTriangle(new Vector3(3, 0, 0), new Vector3(0, 3, 0), new Vector3(0, 0, 3));

        Assert.True(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void TriangleTouchingAFaceOverlaps()
    {
        var triangle = new MeshTriangle(new Vector3(-5, -5, 1), new Vector3(5, -5, 1), new Vector3(0, 5, 1));

        Assert.True(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void FlatBoxIsHitByATriangleThroughIt()
    {
        var line = new Box(new Vector3(0, 0, -5), new Vector3(0, 0, 5));
        var triangle = new MeshTriangle(new Vector3(-5, -5, 0), new Vector3(5, -5, 0), new Vector3(0, 5, 0));

        Assert.True(TriangleBoxOverlap.Overlaps(triangle, line));
    }

    [Fact]
    public void NaNTriangleOverlapsNothing()
    {
        var triangle = new MeshTriangle(new Vector3(float.NaN), Vector3.Zero, Vector3.UnitX);

        Assert.False(TriangleBoxOverlap.Overlaps(triangle, Cube));
    }

    [Fact]
    public void AgreesWithTheTriangleMeshContactTest()
    {
        var random = new Random(5);
        var cubeTree = TestMeshes.Tree(TestMeshes.BoxTriangles(Cube));
        var identity = TestTargets.At(Vector3.Zero);
        for (var i = 0; i < 500; i++)
        {
            var triangle = TestMeshes.RandomTriangle(random, extent: 2.5f, size: 2f);
            var crossesSurface = MeshTouchTest.Touches(cubeTree, identity, TestMeshes.Tree([triangle]), identity, 0f, new TouchScratch());
            var inside = Cube.Contains(triangle.A) && Cube.Contains(triangle.B) && Cube.Contains(triangle.C);

            Assert.Equal(crossesSurface || inside, TriangleBoxOverlap.Overlaps(triangle, Cube));
        }
    }
}
