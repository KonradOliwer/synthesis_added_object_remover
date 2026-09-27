using System.Numerics;
using AddedObjectRemover.NifFooterWorkaround;
using AddedObjectRemover.Tests.Fixtures;
using NiflySharp;

namespace AddedObjectRemover.Tests.Meshes;

public class NifFooterRootTests
{
    private const int FooterRootNodeIndex = 1;
    private const int ShapeIndex = 3;
    private static readonly Box Crate = new(new Vector3(-1, -2, 0), new Vector3(1, 2, 3));
    private static readonly Vector3 OuterOffset = new(10, 0, 0);
    private static readonly Vector3 InnerOffset = new(5, 0, 0);

    [Fact]
    public void FooterOfASavedNifListsBlockZero()
    {
        var nif = CreateNestedCrate();

        var footer = NifFooterReader.Read(TestNifs.Save(nif));

        Assert.NotNull(footer);
        Assert.Equal(nif.Blocks.Count, footer.BlockCount);
        Assert.Equal([0], footer.RootBlockIndices);
    }

    [Fact]
    public void FooterRootNodeOtherThanBlockZeroIsTheMeshRoot()
    {
        var data = TestNifs.WithFooter(TestNifs.Save(CreateNestedCrate()), 1, FooterRootNodeIndex);

        var result = NifGeometryReader.ReadGeometry(data, includeTriangles: true);

        Assert.Equal(NifReadStatus.Success, result.Status);
        Assert.Equal(CrateVertices(InnerOffset), result.Geometry!.Vertices);
        Assert.Equal(new NifRoot(FooterRootNodeIndex, 0), result.FooterRoot);
    }

    [Fact]
    public void FooterListingBlockZeroKeepsTheFirstNodeAsRoot()
    {
        var result = NifGeometryReader.ReadGeometry(TestNifs.Save(CreateNestedCrate()), includeTriangles: true);

        Assert.Equal(NifReadStatus.Success, result.Status);
        Assert.Equal(CrateVertices(OuterOffset + InnerOffset), result.Geometry!.Vertices);
        Assert.Null(result.FooterRoot);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 5000, FooterRootNodeIndex })]
    [InlineData(new[] { 1, 99 })]
    [InlineData(new[] { 1, ShapeIndex })]
    public void UnusableFooterKeepsTheFirstNodeAsRoot(int[] footer)
    {
        var data = TestNifs.WithFooter(TestNifs.Save(CreateNestedCrate()), footer);

        var result = NifGeometryReader.ReadGeometry(data, includeTriangles: true);

        Assert.Equal(NifReadStatus.Success, result.Status);
        Assert.Equal(CrateVertices(OuterOffset + InnerOffset), result.Geometry!.Vertices);
        Assert.Null(result.FooterRoot);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 5000, FooterRootNodeIndex })]
    public void MalformedFooterIsUnknown(int[] footer)
    {
        var data = TestNifs.WithFooter(TestNifs.Save(CreateNestedCrate()), footer);

        Assert.Null(NifFooterReader.Read(data));
    }

    [Fact]
    public void TruncatedHeaderIsUnknown()
    {
        var data = TestNifs.Save(CreateNestedCrate());

        Assert.Null(NifFooterReader.Read(data.AsSpan(0, data.Length / 4)));
    }

    /// <summary>Block 0 root, block 1 node at <see cref="OuterOffset"/>, block 2 node at <see cref="InnerOffset"/>, block 3 the crate.</summary>
    private static NifFile CreateNestedCrate()
    {
        var nif = TestNifs.CreateWithRoot();
        var outer = TestNifs.AddNode(nif, TestNifs.Root(nif), "Outer", OuterOffset);
        var inner = TestNifs.AddNode(nif, outer, "Inner", InnerOffset);
        TestNifs.AddShape(nif, inner, TestMeshes.BoxTriangles(Crate));
        return nif;
    }

    private static IEnumerable<Vector3> CrateVertices(Vector3 offset) =>
        TestMeshes.BoxTriangles(Crate).SelectMany(t => new[] { t.A, t.B, t.C }).Select(v => v + offset);
}
