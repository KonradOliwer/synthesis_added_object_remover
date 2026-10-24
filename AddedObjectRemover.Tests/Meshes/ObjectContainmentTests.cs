using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
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

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Mod, "ObjectContainmentData", Room, FarRoom);

    [Fact]
    public void FindsTheOtherModObjectWhoseMeshSurroundsThePoint()
    {
        var otherModObjects = CreateOtherModObjects(Place(0, Room, new Vector3(1000, 0, 0)), Place(1, Room, Vector3.Zero));

        Assert.Equal(new OtherId(1), FirstCovering(otherModObjects, new Vector3(10, 20, 30)));
        Assert.Equal(new OtherId(0), FirstCovering(otherModObjects, new Vector3(1010, 20, 30)));
        Assert.Null(FirstCovering(otherModObjects, new Vector3(500, 0, 0)));
    }

    [Fact]
    public void ReplacedOtherModObjectsCoverNothing()
    {
        var room = Place(0, Room, Vector3.Zero);
        var replacements = Replacements.Of(otherModObjectCount: 1, [new Replacement(room.Id, new TargetId(0), Distance: 0f, SizeRatio: 1f)]);

        Assert.Null(FirstCovering(CreateOtherModObjects(replacements, room), new Vector3(10, 20, 30)));
        Assert.Equal(room.Id, FirstCovering(CreateOtherModObjects(room), new Vector3(10, 20, 30)));
    }

    [Fact]
    public void ObjectWhoseMeshLiesFarFromItsOriginIsFoundWhereItsMeshIs()
    {
        var otherModObjects = CreateOtherModObjects(Place(0, FarRoom, Vector3.Zero));

        Assert.Equal(new OtherId(0), FirstCovering(otherModObjects, FarOffset + new Vector3(10, 20, 30)));
        Assert.Null(FirstCovering(otherModObjects, new Vector3(10, 20, 30)));
    }

    [Fact]
    public void LowestIdWinsWhenSeveralOtherModObjectsContainThePoint()
    {
        var otherModObjects = CreateOtherModObjects(Place(0, Room, new Vector3(60, 0, 0)), Place(1, Room, new Vector3(-60, 0, 0)));

        Assert.Equal(new OtherId(0), FirstCovering(otherModObjects, new Vector3(0, 20, 30)));
    }

    [Fact]
    public void AnObjectLeftOutIsSkippedForTheNextOneContainingThePoint()
    {
        var otherModObjects = CreateOtherModObjects(Place(0, Room, new Vector3(60, 0, 0)), Place(1, Room, new Vector3(-60, 0, 0)));

        Assert.Equal(new OtherId(1), otherModObjects.FirstCovering(TestTargets.Space, new Vector3(0, 20, 30), id => id != new OtherId(0), new ObjectQueryScratch()));
        Assert.Null(otherModObjects.FirstCovering(TestTargets.Space, new Vector3(0, 20, 30), _ => false, new ObjectQueryScratch()));
    }

    [Fact]
    public void VisibleObjectsOfAnyPluginContainThePointInsideAnyVisibleMesh()
    {
        var objectsOfAnyPlugin = TestScenes.CreateWithSupportOnlyObjects([], [Place(0, Room, Vector3.Zero)], Shapes).VisibleObjectsOfAnyPlugin();

        Assert.True(objectsOfAnyPlugin.Contains(TestTargets.Space, new Vector3(10, 20, 30), new ObjectQueryScratch()));
        Assert.False(objectsOfAnyPlugin.Contains(TestTargets.Space, new Vector3(500, 0, 0), new ObjectQueryScratch()));
    }

    /// <summary>Room 0 is moved 1000 units along X and room 1 taken out; the objects of any plugin as they were stay as they were.</summary>
    [Fact]
    public void VisibleObjectsOfAnyPluginAsTheyStandHoldAMovedObjectWhereItStandsNowAndNoTakenOutOne()
    {
        var moved = Place(0, Room, Vector3.Zero);
        var scene = TestScenes.CreateWithSupportOnlyObjects([], [moved, Place(1, Room, new Vector3(0, 3000, 0))], Shapes);
        var newPlace = new Vector3(1000, 0, 0);
        var changed = new Dictionary<OtherId, OtherObject?> { [moved.Id] = moved with { Position = newPlace }, [new OtherId(1)] = null };

        var asTheyStand = scene.VisibleObjectsOfAnyPluginAsTheyStand(changed);

        var scratch = new ObjectQueryScratch();
        Assert.True(asTheyStand.Contains(TestTargets.Space, newPlace + new Vector3(10, 20, 30), scratch));
        Assert.False(asTheyStand.Contains(TestTargets.Space, new Vector3(10, 20, 30), scratch));
        Assert.False(asTheyStand.Contains(TestTargets.Space, new Vector3(10, 3020, 30), scratch));
        Assert.Equal([moved.Id], Overlapping(asTheyStand, new Box(newPlace, newPlace)));
        Assert.Empty(Overlapping(asTheyStand, new Box(Vector3.Zero, Vector3.Zero)));
        Assert.Equal(newPlace, asTheyStand.Get(moved.Id).Position);
        Assert.Equal([moved.Id], Overlapping(scene.VisibleObjectsOfAnyPlugin(), new Box(Vector3.Zero, Vector3.Zero)));
    }

    private static List<OtherId> Overlapping(IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin, Box area)
    {
        var found = new List<OtherId>();
        objectsOfAnyPlugin.Overlapping(TestTargets.Space, area, new ObjectQueryScratch(), found);
        return found;
    }

    /// <summary>Rooms are turned so the containment test runs in a rotated frame; the far room is not, so its mesh stays at <see cref="FarOffset"/>.</summary>
    private static OtherObject Place(int id, TestStatic model, Vector3 position) =>
        TestShapes.Placed(Mod, id, model.Base, position, zRadians: model == FarRoom ? 0f : 0.7f);

    private static IObjectsThatCanCauseRemovals CreateOtherModObjects(params OtherObject[] otherModObjects) =>
        CreateOtherModObjects(Replacements.None(otherModObjects.Length), otherModObjects);

    private static IObjectsThatCanCauseRemovals CreateOtherModObjects(Replacements replacements, params OtherObject[] otherModObjects) =>
        TestScenes.Create([], otherModObjects, Shapes).ObjectsThatCanCauseRemovals(replacements, NpcHandling.AlwaysLikeAnyObject);

    private static OtherId? FirstCovering(IObjectsThatCanCauseRemovals otherModObjects, Vector3 point) =>
        otherModObjects.FirstCovering(TestTargets.Space, point, _ => true, new ObjectQueryScratch());
}
