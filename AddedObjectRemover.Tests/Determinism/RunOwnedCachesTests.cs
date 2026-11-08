using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Determinism;

public class RunOwnedCachesTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Cache.esp");
    private static readonly Box Crate = new(new System.Numerics.Vector3(-1, -2, 0), new System.Numerics.Vector3(1, 2, 3));

    private static BoundsStats StatsOf(IBaseObjectShapes shapes) => shapes.CountBounds();

    [Fact]
    public void TwoSetsOfCachesInOneProcessShareNoState()
    {
        var model = new TestStatic(new FormKey(Mod, 0x800), @"meshes\crate.nif", TestMeshes.BoxTriangles(Crate));
        var first = TestShapes.Create(Mod, nameof(RunOwnedCachesTests), model);
        var second = TestShapes.Create(Mod, nameof(RunOwnedCachesTests), model);

        first.Of(model.Base);

        Assert.Equal(1, StatsOf(first).BasesFromNif);
        Assert.Equal(1, StatsOf(first).ModelsRead);
        Assert.Equal(0, StatsOf(second).BasesFromNif);
        Assert.Equal(0, StatsOf(second).ModelsRead);

        second.Of(model.Base);

        Assert.Equal(1, StatsOf(second).BasesFromNif);
        Assert.Equal(1, StatsOf(second).ModelsRead);
        Assert.Equal(1, StatsOf(first).ModelsRead);
    }

    [Fact]
    public void TwoNifReadersInOneProcessEachReadTheSameGeometry()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));
        var bytes = TestNifs.Save(nif);

        var firstRead = new NifGeometryReader().ReadGeometry(bytes, includeTriangles: false, BaseObjectShapes.SolidShapes);
        var secondRead = new NifGeometryReader().ReadGeometry(bytes, includeTriangles: false, BaseObjectShapes.SolidShapes);

        Assert.Equal(MeshReadStatus.Success, firstRead.Status);
        Assert.Equal(MeshReadStatus.Success, secondRead.Status);
        Assert.Equal((Crate.Min, Crate.Max), (firstRead.Geometry!.Min, firstRead.Geometry.Max));
        Assert.Equal((Crate.Min, Crate.Max), (secondRead.Geometry!.Min, secondRead.Geometry.Max));
    }
}
