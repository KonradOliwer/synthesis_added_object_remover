using System.Numerics;
using System.Reflection;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;
using NiflySharp.Blocks;

namespace AddedObjectRemover.Tests.Meshes;

public class NifReadingTests
{
    private static readonly Box Crate = new(new Vector3(-1, -2, 0), new Vector3(1, 2, 3));

    [Fact]
    public void ShapeUnderANodeIsReadInRootSpace()
    {
        var nif = TestNifs.CreateWithRoot();
        var node = TestNifs.AddNode(nif, TestNifs.Root(nif), "Crate", new Vector3(10, 0, 0));
        TestNifs.AddShape(nif, node, TestMeshes.BoxTriangles(Crate));

        var result = new NifGeometryReader().ReadGeometry(TestNifs.Save(nif), includeTriangles: true, BaseObjectRules.SolidShapes);

        Assert.Equal(MeshReadStatus.Success, result.Status);
        var geometry = result.Geometry!;
        var shifted = TestMeshes.BoxTriangles(Crate).SelectMany(t => new[] { t.A, t.B, t.C }).Select(v => v + new Vector3(10, 0, 0));
        Assert.Equal(shifted, geometry.Triangles.Vertices);
        Assert.Equal(Crate.Min + new Vector3(10, 0, 0), geometry.Min);
        Assert.Equal(Crate.Max + new Vector3(10, 0, 0), geometry.Max);
        Assert.Equal(12, geometry.Triangles.TriangleCount);
    }

    [Fact]
    public void EveryShapeStartsAPart()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate).Take(5).ToList());
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));

        var result = new NifGeometryReader().ReadGeometry(TestNifs.Save(nif), includeTriangles: true, BaseObjectRules.SolidShapes);

        Assert.Equal(MeshReadStatus.Success, result.Status);
        Assert.Equal(new[] { 0, 12, 17 }, result.Geometry!.Triangles.PartFirstTriangles);
        Assert.Equal(29, result.Geometry.Triangles.TriangleCount);
    }

    [Fact]
    public void RootNamedAsEditorMarkerHasNoRenderGeometry()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.Root(nif).Name = new NiflySharp.NiStringRef("EditorMarker");
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));

        var result = new NifGeometryReader().ReadGeometry(TestNifs.Save(nif), includeTriangles: false, BaseObjectRules.SolidShapes);

        Assert.Equal(MeshReadStatus.NoRenderGeometry, result.Status);
    }

    [Fact]
    public void AnInclusionNotSkippingEditorMarkerNodesCountsTheirShapes()
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.Root(nif).Name = new NiflySharp.NiStringRef("EditorMarker");
        TestNifs.AddShape(nif, TestNifs.Root(nif), TestMeshes.BoxTriangles(Crate));
        var inclusion = BaseObjectRules.SolidShapes with { SkippedAncestorKinds = new HashSet<MeshShapeKind>() };

        var result = new NifGeometryReader().ReadGeometry(TestNifs.Save(nif), includeTriangles: false, inclusion);

        Assert.Equal(MeshReadStatus.Success, result.Status);
    }

    [Fact]
    public void AnInclusionWithoutTheRetryLeavesAHiddenOnlyMeshEmpty()
    {
        var nif = TestNifs.CreateWithRoot();
        var root = TestNifs.Root(nif);
        TestNifs.AddShape(nif, root, TestMeshes.BoxTriangles(Crate));
        root.Flags_ui |= 1;
        var data = TestNifs.Save(nif);

        var retried = new NifGeometryReader().ReadGeometry(data, includeTriangles: false, BaseObjectRules.SolidShapes);
        var notRetried = new NifGeometryReader().ReadGeometry(data, includeTriangles: false, BaseObjectRules.SolidShapes with { RetryIncludingHiddenWhenEmpty = false });

        Assert.Equal(MeshReadStatus.Success, retried.Status);
        Assert.Equal(MeshReadStatus.NoRenderGeometry, notRetried.Status);
    }

    [Fact]
    public void HiddenRootHidesItsShapesInTheFirstPass()
    {
        var nif = TestNifs.CreateWithRoot();
        var root = TestNifs.Root(nif);
        TestNifs.AddShape(nif, root, TestMeshes.BoxTriangles(Crate));
        root.Flags_ui |= 1;
        var blocks = nif.Blocks;
        var parentOf = new Dictionary<int, int> { [1] = 0 };

        var hidden = NifShapeCollector.Collect(new RenderGeometryTypes(), blocks, parentOf, 0, AvObjectFlags.For(nif), BaseObjectRules.SolidShapes, includeTriangles: false);
        var shown = NifShapeCollector.Collect(new RenderGeometryTypes(), blocks, parentOf, 0, AvObjectFlags.For(nif), BaseObjectRules.SolidShapes.WithHiddenIncluded(), includeTriangles: false);

        Assert.False(hidden.Bounds.Any);
        Assert.Equal(1, hidden.Stats.HiddenAncestor);
        Assert.True(shown.Bounds.Any);
    }

    [Fact]
    public void MatchingStripLengthsGiveTriangles()
    {
        var strips = CreateStrips(points: [0, 1, 2, 3], stripLengths: [4]);

        var triangles = NifShapes.GetTriangles(strips, out var fault);

        Assert.Equal(StripFault.None, fault);
        Assert.Equal(2, triangles!.Count);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3)]
    public void StripLengthsNotAddingUpToThePointsAreReported(int stripLength)
    {
        var strips = CreateStrips(points: [0, 1, 2, 3], stripLengths: [(ushort)stripLength]);

        var triangles = NifShapes.GetTriangles(strips, out var fault);

        Assert.Equal(StripFault.LengthsMismatch, fault);
        Assert.Null(triangles);
    }

    private static NiTriStrips CreateStrips(List<ushort> points, List<ushort> stripLengths)
    {
        var data = new NiTriStripsData();
        SetField(data, "_points", points);
        SetField(data, "_stripLengths", stripLengths);
        return new NiTriStrips { GeometryData = data };
    }

    private static void SetField(NiTriStripsData data, string name, List<ushort> value)
    {
        var field = typeof(NiTriStripsData).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(data, value);
    }
}
