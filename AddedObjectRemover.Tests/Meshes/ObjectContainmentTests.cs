using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Meshes;

public class ObjectContainmentTests
{
    private const string RoomModel = @"test\room.nif";

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Rooms.esp");
    private static readonly FormKey RoomBase = new(Mod, 0x801);
    private static readonly BaseRef Room = new(RoomBase, typeof(IStaticGetter));
    private static readonly Box RoomBox = new(new Vector3(-100), new Vector3(100));

    [Fact]
    public void FindsTheObjectWhoseMeshSurroundsThePoint()
    {
        var (containment, index) = CreateRooms(new Vector3(1000, 0, 0), Vector3.Zero);

        Assert.Equal(1, containment.FindContainingVisible(index, new Vector3(10, 20, 30), skipReplaced: false));
        Assert.Equal(0, containment.FindContainingVisible(index, new Vector3(1010, 20, 30), skipReplaced: false));
        Assert.Equal(-1, containment.FindContainingVisible(index, new Vector3(500, 0, 0), skipReplaced: false));
    }

    [Fact]
    public void ReplacedObjectsAreSkippedOnRequest()
    {
        var (containment, index) = CreateRooms(Vector3.Zero);
        Assert.True(index.TryMarkReplaced(0));

        Assert.Equal(-1, containment.FindContainingVisible(index, new Vector3(10, 20, 30), skipReplaced: true));
        Assert.Equal(0, containment.FindContainingVisible(index, new Vector3(10, 20, 30), skipReplaced: false));
    }

    private static (ObjectContainment Containment, OtherObjectIndex Index) CreateRooms(params Vector3[] positions)
    {
        var shapes = CreateShapes();
        var objects = positions
            .Select((position, i) => new OtherObject(
                new FormKey(Mod, 0x900 + (uint)i), Mod, EditorId: null, Room, position, new P3Float(0, 0, 0.7f), 1f,
                IsPrimitive: false, HasMapMarker: false))
            .ToList();
        var containment = new ObjectContainment(shapes, new TriangleTreeCache(shapes.ReadGeometry));
        return (containment, OtherObjectIndex.CreateUncounted(objects, shapes));
    }

    private static BaseObjectShapeProvider CreateShapes()
    {
        var mod = new SkyrimMod(Mod, SkyrimRelease.SkyrimSE);
        mod.Statics.Add(new Static(RoomBase, SkyrimRelease.SkyrimSE) { Model = new Model { File = RoomModel } });

        var dataPath = WriteRoomMesh();
        var messages = new MeshMessageLog(enabled: false);
        var meshFiles = new MeshFileSource(dataPath, GameRelease.SkyrimSE, [Mod], messages);
        return new BaseObjectShapeProvider(mod.ToImmutableLinkCache(), meshFiles, messages);
    }

    /// <summary>A Data folder next to the test binaries holding the room as a loose mesh.</summary>
    private static string WriteRoomMesh()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(RoomBox));

        var dataPath = Path.Combine(AppContext.BaseDirectory, "ObjectContainmentData");
        var meshPath = Path.Combine(dataPath, MeshFileSource.NormalizeMeshPath(RoomModel));
        Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
        File.WriteAllBytes(meshPath, TestNifs.Save(nif));
        return dataPath;
    }
}
