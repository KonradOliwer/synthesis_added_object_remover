using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Meshes;

public class ObjectContainmentTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Rooms.esp");
    private static readonly Box RoomBox = new(new Vector3(-100), new Vector3(100));

    /// <summary>Farther from the origin than an exterior cell, so only the mesh bounds can find it.</summary>
    private static readonly Vector3 FarOffset = new(10000, 0, 0);

    private static readonly TestStatic Room = new(new FormKey(Mod, 0x801), @"test\room.nif", TestMeshes.BoxTriangles(RoomBox));

    private static readonly TestStatic FarRoom = new(
        new FormKey(Mod, 0x802), @"test\farroom.nif", TestMeshes.BoxTriangles(new Box(RoomBox.Min + FarOffset, RoomBox.Max + FarOffset)));

    [Fact]
    public void FindsTheObjectWhoseMeshSurroundsThePoint()
    {
        var (containment, index) = CreateIndex(Place(Room, new Vector3(1000, 0, 0)), Place(Room, Vector3.Zero));

        Assert.Equal(1, containment.FindContainingVisible(index, new Vector3(10, 20, 30), replacements: null, new SpatialQueryScratch()));
        Assert.Equal(0, containment.FindContainingVisible(index, new Vector3(1010, 20, 30), replacements: null, new SpatialQueryScratch()));
        Assert.Equal(-1, containment.FindContainingVisible(index, new Vector3(500, 0, 0), replacements: null, new SpatialQueryScratch()));
    }

    [Fact]
    public void ReplacedObjectsAreSkippedOnRequest()
    {
        var (containment, index) = CreateIndex(Place(Room, Vector3.Zero));
        var replacements = Replacements.Of(rivalCount: 1, [new Replacement(index[0].Id, new TargetId(0), Distance: 0f, SizeRatio: 1f)]);

        Assert.Equal(-1, containment.FindContainingVisible(index, new Vector3(10, 20, 30), replacements, new SpatialQueryScratch()));
        Assert.Equal(0, containment.FindContainingVisible(index, new Vector3(10, 20, 30), replacements: null, new SpatialQueryScratch()));
    }

    [Fact]
    public void ObjectWhoseMeshLiesFarFromItsOriginIsFoundWhereItsMeshIs()
    {
        var (containment, index) = CreateIndex(Place(FarRoom, Vector3.Zero));

        Assert.Equal(0, containment.FindContainingVisible(index, FarOffset + new Vector3(10, 20, 30), replacements: null, new SpatialQueryScratch()));
        Assert.Equal(-1, containment.FindContainingVisible(index, new Vector3(10, 20, 30), replacements: null, new SpatialQueryScratch()));
    }

    [Fact]
    public void LowestIndexWinsWhenSeveralObjectsContainThePoint()
    {
        var (containment, index) = CreateIndex(Place(Room, new Vector3(60, 0, 0)), Place(Room, new Vector3(-60, 0, 0)));

        Assert.Equal(0, containment.FindContainingVisible(index, new Vector3(0, 20, 30), replacements: null, new SpatialQueryScratch()));
    }

    /// <summary>Rooms are turned so the containment test runs in a rotated frame; the far room is not, so its mesh stays at <see cref="FarOffset"/>.</summary>
    private static OtherObject Place(TestStatic model, Vector3 position) =>
        TestShapes.Placed(Mod, 0, model.Ref, position, zRadians: model == FarRoom ? 0f : 0.7f);

    private static (ObjectContainment Containment, OtherObjectIndex Index) CreateIndex(params OtherObject[] objects)
    {
        var shapes = TestShapes.Create(Mod, "ObjectContainmentData", Room, FarRoom);
        var containment = new ObjectContainment(shapes, new TriangleTreeCache(shapes.ReadGeometry));
        return (containment, OtherObjectIndex.CreateUncounted(objects, shapes, new ParallelOptions()));
    }
}
