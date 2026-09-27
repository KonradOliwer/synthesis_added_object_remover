using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>An other object lying entirely inside the ObjectShape zone, without any triangles intersecting.</summary>
public class ShapeZoneContactTests
{
    private static readonly Box Room = new(new Vector3(-10), new Vector3(10));
    private static readonly ShapeZone Zone = ShapeZone.Create(Room, TestTargets.At(Vector3.Zero), multiplier: 0f);
    private static readonly PlacedTransform Identity = TestTargets.At(Vector3.Zero);

    private static readonly List<MeshTriangle> PieceOutside = TestMeshes.BoxTriangles(new Box(new Vector3(40, -1, -1), new Vector3(42, 1, 1)));
    private static readonly List<MeshTriangle> PieceInside = TestMeshes.BoxTriangles(new Box(new Vector3(-1), new Vector3(1)));

    [Fact]
    public void ClosedZoneHoldsAnObjectWithOnlyItsSecondPartInside()
    {
        var other = TestMeshes.PartsTree(PieceOutside, PieceInside);

        Assert.True(ShapeZoneContact.Reaches(BoxMesh.CreateTree(Room), Zone, other, Identity, new TouchScratch()));
    }

    [Fact]
    public void ClosedZoneDoesNotHoldAnObjectWithEveryPartOutside()
    {
        var other = TestMeshes.PartsTree(PieceOutside, TestMeshes.BoxTriangles(new Box(new Vector3(-42, -1, -1), new Vector3(-40, 1, 1))));

        Assert.False(ShapeZoneContact.Reaches(BoxMesh.CreateTree(Room), Zone, other, Identity, new TouchScratch()));
    }

    [Fact]
    public void OpenZoneHoldsAnObjectWhoseWholeBoxIsInside()
    {
        var bubble = OpenTopRoom();

        Assert.False(bubble.IsClosed);
        Assert.True(ShapeZoneContact.Reaches(bubble, Zone, TestMeshes.Tree(PieceInside), Identity, new TouchScratch()));
    }

    [Fact]
    public void OpenZoneDoesNotHoldAnObjectStickingOutOfItsBox()
    {
        // Rises through the open top without touching the walls; its first vertex lies inside the zone's box.
        var other = TestMeshes.Tree(TestMeshes.BoxTriangles(new Box(new Vector3(-2, -2, 8), new Vector3(2, 2, 14))));

        Assert.False(ShapeZoneContact.Reaches(OpenTopRoom(), Zone, other, Identity, new TouchScratch()));
    }

    private static MeshTriangleTree OpenTopRoom() => TestMeshes.Tree(TestMeshes.BoxWithoutFace(Room, v => v.Z == Room.Max.Z));
}
