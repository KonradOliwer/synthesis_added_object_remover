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
    public void ClosedBoxEnclosesAPointOffItsDiagonals()
    {
        var tree = BoxMesh.CreateTree(TestMeshes.UnitCube);
        Assert.True(PointContactTest.IsEnclosed(tree, new Vector3(0.3f, 0.2f, 0.4f), []));
        Assert.False(PointContactTest.IsEnclosed(tree, new Vector3(1.5f, 0.5f, 0.5f), []));
    }

    [Fact]
    [Trait(KnownBug.Trait, "A2: crossings exactly on a shared triangle edge are not counted, so the centre of a box split along its face diagonals is not enclosed")]
    public void BoxCentreOnFaceDiagonalsIsNotEnclosed() =>
        Assert.False(PointContactTest.IsEnclosed(BoxMesh.CreateTree(TestMeshes.UnitCube), new Vector3(0.5f), []));

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
