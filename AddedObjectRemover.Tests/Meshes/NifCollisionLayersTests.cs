using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using NiflySharp;
using NiflySharp.Enums;

namespace AddedObjectRemover.Tests.Meshes;

public class NifCollisionLayersTests
{
    private static readonly Box Rock = new(new Vector3(-10, -10, 0), new Vector3(10, 10, 20));

    [Fact]
    public void TheRigidBodyLayersOfTheRootAndItsNodesAreReadInOrder()
    {
        var nif = CreateRock();
        var branch = TestNifs.AddNode(nif, TestNifs.Root(nif), "Branch", Vector3.Zero);
        TestNifs.AddRigidBodyCollision(nif, branch, SkyrimLayer.SKYL_TREES);
        TestNifs.AddRigidBodyCollision(nif, TestNifs.Root(nif), SkyrimLayer.SKYL_STATIC);

        Assert.Equal([CollisionLayer.Static, CollisionLayer.Trees], ReadLayers(nif));
    }

    [Fact]
    public void AMeshWithoutCollisionHasNoLayers()
    {
        Assert.Equal([], ReadLayers(CreateRock()));
    }

    [Fact]
    public void CollisionUnderAnEditorMarkerNodeIsLeftOut()
    {
        var nif = CreateRock();
        var marker = TestNifs.AddNode(nif, TestNifs.Root(nif), "EditorMarker", Vector3.Zero);
        TestNifs.AddRigidBodyCollision(nif, marker, SkyrimLayer.SKYL_STATIC);

        Assert.Equal([], ReadLayers(nif));
    }

    [Fact]
    public void CollisionOfAHiddenNodeCounts()
    {
        var nif = CreateRock();
        var hidden = TestNifs.AddNode(nif, TestNifs.Root(nif), "Collision", Vector3.Zero);
        hidden.Flags_ui |= 1;
        TestNifs.AddRigidBodyCollision(nif, hidden, SkyrimLayer.SKYL_STATIC);

        Assert.Equal([CollisionLayer.Static], ReadLayers(nif));
    }

    [Fact]
    public void APhantomCollisionObjectIsLeftOut()
    {
        var nif = CreateRock();
        TestNifs.AddPhantomCollision(nif, TestNifs.Root(nif), SkyrimLayer.SKYL_TRIGGER);

        Assert.Equal([], ReadLayers(nif));
    }

    [Fact]
    public void TheMeshReaderKeepsTheLayersWithTheBounds()
    {
        var nif = CreateRock();
        TestNifs.AddRigidBodyCollision(nif, TestNifs.Root(nif), SkyrimLayer.SKYL_CLUTTER);

        var dataPath = Path.Combine(AppContext.BaseDirectory, "CollisionLayersData");
        var meshFile = Path.Combine(dataPath, "meshes", "rock.nif");
        Directory.CreateDirectory(Path.GetDirectoryName(meshFile)!);
        File.WriteAllBytes(meshFile, TestNifs.Save(nif));
        var meshFiles = new MeshFilesFactory(dataPath, GameRelease.SkyrimSE, []).Open(BaseObjectShapes.SolidShapes, new UnexpectedErrors());

        var bounds = meshFiles.Bounds.Of(@"meshes\rock.nif");

        Assert.Equal([CollisionLayer.Clutter], bounds.CollisionLayers);
    }

    [Fact]
    public void AnUnloadableMeshHasNoCollisionLayers()
    {
        var result = new NifGeometryReader().ReadGeometry([1, 2, 3], includeTriangles: false, BaseObjectShapes.SolidShapes);

        Assert.Null(result.CollisionLayers);
    }

    private static NifFile CreateRock()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Rock));
        return nif;
    }

    private static CollisionLayer[] ReadLayers(NifFile nif)
    {
        var result = new NifGeometryReader().ReadGeometry(TestNifs.Save(nif), includeTriangles: false, BaseObjectShapes.SolidShapes);
        Assert.Equal(MeshReadStatus.Success, result.Status);
        return [.. result.CollisionLayers!.Value];
    }
}
