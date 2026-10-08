using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Determinism;

public class RunOwnedCachesTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Cache.esp");
    private static readonly Box Crate = new(new System.Numerics.Vector3(-1, -2, 0), new System.Numerics.Vector3(1, 2, 3));

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

    private static BoundsStats StatsOf(IBaseObjectShapes shapes)
    {
        var meshFiles = TestShapes.MeshFilesOf(shapes);
        return BaseObjectShapes.CountBounds(shapes.Computed(), meshFiles.Bounds.Computed(), meshFiles.ArchivesIndexed);
    }

    [Fact]
    public void TwoNifReadersEachKeepTheirOwnWarmUpAndShapeClassMemo()
    {
        var first = new NifGeometryReader();
        var second = new NifGeometryReader();
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));

        first.ReadGeometry(TestNifs.Save(nif), includeTriangles: false, BaseObjectRules.SolidShapes);

        Assert.True(first.HasWarmedUp);
        Assert.True(first.ShapeClassesDecided > 0);
        Assert.False(second.HasWarmedUp);
        Assert.Equal(0, second.ShapeClassesDecided);

        second.ReadGeometry(TestNifs.Save(nif), includeTriangles: false, BaseObjectRules.SolidShapes);

        Assert.True(second.HasWarmedUp);
        Assert.Equal(first.ShapeClassesDecided, second.ShapeClassesDecided);
    }
}
