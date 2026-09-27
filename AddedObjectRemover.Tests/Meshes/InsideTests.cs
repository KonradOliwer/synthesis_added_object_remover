using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Noggog;

namespace AddedObjectRemover.Tests.Meshes;

public class InsideTests
{
    private static readonly Box Room = new(new Vector3(-10), new Vector3(10));

    [Fact]
    public void ClosedBoxSurroundsItsInside() =>
        Assert.True(SurroundingRayTest.IsSurrounded(BoxMesh.CreateTree(Room), new Vector3(1, 2, 3), Mat3.Identity, []));

    [Fact]
    public void PointOutsideTheBoundsIsNotSurrounded() =>
        Assert.False(SurroundingRayTest.IsSurrounded(BoxMesh.CreateTree(Room), new Vector3(11, 0, 0), Mat3.Identity, []));

    [Fact]
    public void RotatedBoxStillSurroundsItsInside()
    {
        var rotation = AddedObjectRemover.Geometry.RotationFromEuler(new P3Float(0, 0, MathF.PI / 4));
        Assert.True(SurroundingRayTest.IsSurrounded(BoxMesh.CreateTree(Room), new Vector3(1, 2, 3), rotation, []));
    }

    [Fact]
    public void RoofAloneDoesNotSurround()
    {
        var roof = TestMeshes.BoxTriangles(Room).Where(t => t.A.Z == Room.Max.Z && t.B.Z == Room.Max.Z && t.C.Z == Room.Max.Z).ToList();
        var tree = TestMeshes.Tree(roof);
        Assert.False(SurroundingRayTest.IsSurrounded(tree, new Vector3(1, 2, Room.Max.Z), Mat3.Identity, []));
    }

    [Fact]
    public void BoxWithoutAWallDoesNotSurround()
    {
        var tree = TestMeshes.Tree(TestMeshes.BoxWithoutFace(Room, v => v.X == Room.Max.X));
        Assert.False(SurroundingRayTest.IsSurrounded(tree, new Vector3(1, 2, 3), Mat3.Identity, []));
    }

    [Fact]
    public void BoxWithoutAFloorStillSurrounds()
    {
        var tree = TestMeshes.Tree(TestMeshes.BoxWithoutFace(Room, v => v.Z == Room.Min.Z));
        Assert.True(SurroundingRayTest.IsSurrounded(tree, new Vector3(1, 2, 3), Mat3.Identity, []));
    }

    [Fact]
    public void RaysThroughFaceDiagonalsHitTheBox() =>
        Assert.True(SurroundingRayTest.IsSurrounded(BoxMesh.CreateTree(Room), Vector3.Zero, Mat3.Identity, []));

    [Fact]
    public void RayThroughATriangleCornerHitsIt()
    {
        var triangle = new MeshTriangle(new Vector3(10, 0, 0), new Vector3(10, 5, 0), new Vector3(10, 0, 5));
        Assert.True(SurroundingRayTest.RayHitsTriangle(Vector3.Zero, Vector3.UnitX, triangle));
        Assert.False(SurroundingRayTest.RayHitsTriangle(Vector3.Zero, -Vector3.UnitX, triangle));
    }

    [Fact]
    public void RayPastATinyTriangleDoesNotHitIt()
    {
        // The determinant is subnormal, so its inverse is infinite and the barycentric weights come out NaN.
        var triangle = new MeshTriangle(new Vector3(10, 1e-30f, 0), new Vector3(10, 1e-20f, 0), new Vector3(10, 1e-30f, 1e-20f));
        Assert.False(SurroundingRayTest.RayHitsTriangle(Vector3.Zero, Vector3.UnitX, triangle));
    }

    [Fact]
    public void TriangleWithANaNCornerIsNeverHit()
    {
        var triangle = new MeshTriangle(new Vector3(10, -5, -5), new Vector3(10, 5, float.NaN), new Vector3(10, 0, 5));
        Assert.False(SurroundingRayTest.RayHitsTriangle(Vector3.Zero, Vector3.UnitX, triangle));
    }

    [Fact]
    public void ClosedBoxEnclosesAPointOffItsDiagonals()
    {
        var tree = BoxMesh.CreateTree(TestMeshes.UnitCube);
        Assert.True(PointContactTest.IsEnclosed(tree, new Vector3(0.3f, 0.2f, 0.4f), []));
        Assert.False(PointContactTest.IsEnclosed(tree, new Vector3(1.5f, 0.5f, 0.5f), []));
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.3f, 0.3f, 0.5f)]
    [InlineData(0.5f, 0.3f, 0.5f)]
    [InlineData(0.25f, 0.5f, 0.75f)]
    public void PointsOnFaceDiagonalsAreEnclosed(float x, float y, float z) =>
        Assert.True(PointContactTest.IsEnclosed(BoxMesh.CreateTree(TestMeshes.UnitCube), new Vector3(x, y, z), []));

    [Fact]
    public void LineThroughAVertexSharedByAFanCrossesOnce()
    {
        var top = new Vector3(0.5f, 0.5f, 1);
        var corners = new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1) };
        var fan = Enumerable.Range(0, corners.Length).Select(i => new MeshTriangle(top, corners[i], corners[(i + 1) % corners.Length]));
        var tree = TestMeshes.Tree(TestMeshes.BoxWithoutFace(TestMeshes.UnitCube, v => v.Z == 1).Concat(fan).ToList());

        Assert.True(PointContactTest.IsEnclosed(tree, new Vector3(0.5f, 0.5f, 0.5f), []));
        Assert.False(PointContactTest.IsEnclosed(tree, new Vector3(0.5f, 0.5f, 1.5f), []));
    }

    [Fact]
    public void EnclosureMatchesTheBoxForRotatedClosedBoxes()
    {
        var random = new Random(3);
        for (var i = 0; i < 20; i++)
        {
            var rotation = AddedObjectRemover.Geometry.RotationFromEuler(
                new P3Float(TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random), TestMeshes.RandomAngle(random)));
            var triangles = TestMeshes.BoxTriangles(Room)
                .Select(t => new MeshTriangle(rotation.Transform(t.A), rotation.Transform(t.B), rotation.Transform(t.C)))
                .ToList();
            var tree = TestMeshes.Tree(triangles);
            for (var j = 0; j < 20; j++)
            {
                var local = TestMeshes.RandomVector(random, 12);
                var inside = Vector3.Abs(local) is { X: < 9.99f, Y: < 9.99f, Z: < 9.99f };
                var outside = Vector3.Abs(local) is { X: > 10.01f } or { Y: > 10.01f } or { Z: > 10.01f };
                if (!inside && !outside) continue;
                Assert.Equal(inside, PointContactTest.IsEnclosed(tree, rotation.Transform(local), []));
            }
        }
    }

    [Fact]
    public void OpenBoxEnclosesNothing()
    {
        var tree = TestMeshes.Tree(TestMeshes.BoxWithoutFace(TestMeshes.UnitCube, v => v.Z == 1));
        Assert.False(PointContactTest.IsEnclosed(tree, new Vector3(0.3f, 0.2f, 0.4f), []));
    }

    [Fact]
    public void PointNearTheSurfaceIsInContact()
    {
        var tree = BoxMesh.CreateTree(TestMeshes.UnitCube);
        Assert.True(PointContactTest.IsInContact(tree, new Vector3(0.3f, 0.2f, 1.05f), 0.1f, []));
        Assert.False(PointContactTest.IsInContact(tree, new Vector3(0.3f, 0.2f, 1.2f), 0.1f, []));
    }
}
