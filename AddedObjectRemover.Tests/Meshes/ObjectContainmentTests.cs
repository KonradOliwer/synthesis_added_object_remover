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

    private static readonly ShapeCatalog Shapes = TestShapes.Create(Mod, "ObjectContainmentData", Room, FarRoom);

    [Fact]
    public void FindsTheRivalWhoseMeshSurroundsThePoint()
    {
        var rivals = CreateRivals(Place(0, Room, new Vector3(1000, 0, 0)), Place(1, Room, Vector3.Zero));

        Assert.Equal(new OtherId(1), FirstCovering(rivals, new Vector3(10, 20, 30)));
        Assert.Equal(new OtherId(0), FirstCovering(rivals, new Vector3(1010, 20, 30)));
        Assert.Null(FirstCovering(rivals, new Vector3(500, 0, 0)));
    }

    [Fact]
    public void ReplacedRivalsCoverNothing()
    {
        var room = Place(0, Room, Vector3.Zero);
        var replacements = Replacements.Of(rivalCount: 1, [new Replacement(room.Id, new TargetId(0), Distance: 0f, SizeRatio: 1f)]);

        Assert.Null(FirstCovering(CreateRivals(replacements, room), new Vector3(10, 20, 30)));
        Assert.Equal(room.Id, FirstCovering(CreateRivals(room), new Vector3(10, 20, 30)));
    }

    [Fact]
    public void ObjectWhoseMeshLiesFarFromItsOriginIsFoundWhereItsMeshIs()
    {
        var rivals = CreateRivals(Place(0, FarRoom, Vector3.Zero));

        Assert.Equal(new OtherId(0), FirstCovering(rivals, FarOffset + new Vector3(10, 20, 30)));
        Assert.Null(FirstCovering(rivals, new Vector3(10, 20, 30)));
    }

    [Fact]
    public void LowestIdWinsWhenSeveralRivalsContainThePoint()
    {
        var rivals = CreateRivals(Place(0, Room, new Vector3(60, 0, 0)), Place(1, Room, new Vector3(-60, 0, 0)));

        Assert.Equal(new OtherId(0), FirstCovering(rivals, new Vector3(0, 20, 30)));
    }

    [Fact]
    public void SolidsContainThePointInsideAnyVisibleMesh()
    {
        var solids = TestScenes.CreateWithBackdrop([], [Place(0, Room, Vector3.Zero)], Shapes).Solids();

        Assert.True(solids.Contains(TestTargets.Space, new Vector3(10, 20, 30), new SpatialQueryScratch()));
        Assert.False(solids.Contains(TestTargets.Space, new Vector3(500, 0, 0), new SpatialQueryScratch()));
    }

    /// <summary>Rooms are turned so the containment test runs in a rotated frame; the far room is not, so its mesh stays at <see cref="FarOffset"/>.</summary>
    private static OtherObject Place(int id, TestStatic model, Vector3 position) =>
        TestShapes.Placed(Mod, id, model.Ref, position, zRadians: model == FarRoom ? 0f : 0.7f);

    private static IActiveRivals CreateRivals(params OtherObject[] rivals) => CreateRivals(Replacements.None(rivals.Length), rivals);

    private static IActiveRivals CreateRivals(Replacements replacements, params OtherObject[] rivals) =>
        TestScenes.Create([], rivals, Shapes).ActiveRivals(replacements, NpcHandling.CountLikeObjects);

    private static OtherId? FirstCovering(IActiveRivals rivals, Vector3 point) =>
        rivals.FirstCovering(TestTargets.Space, point, new SpatialQueryScratch());
}
