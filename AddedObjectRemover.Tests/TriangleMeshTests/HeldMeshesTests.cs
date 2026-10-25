using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class HeldMeshesTests
{
    private const string BoxPath = @"meshes\box.nif";
    private const string OtherPath = @"meshes\other.nif";

    [Fact]
    public void AMeshUsedAgainUnderAnyCaseOfItsPathTakesNoNewLease()
    {
        var meshes = NewMeshes();
        var held = new HeldMeshes(meshes);

        var first = held.Get(BoxPath);
        var again = held.Get(BoxPath.ToUpperInvariant());

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal((1, 1), (meshes.Acquired, meshes.Open));
    }

    [Fact]
    public void ReleaseEndsEveryLeaseAndALaterUseTakesANewOne()
    {
        var meshes = NewMeshes();
        var held = new HeldMeshes(meshes);
        held.Get(BoxPath);
        held.Get(OtherPath);

        held.Release();

        Assert.Equal((2, 0), (meshes.Acquired, meshes.Open));
        held.Get(BoxPath);
        Assert.Equal((3, 1), (meshes.Acquired, meshes.Open));
    }

    private static LeaseCountingMeshes NewMeshes() => new(_ => BoxMesh.CreateTriangles(TestMeshes.UnitCube));
}
