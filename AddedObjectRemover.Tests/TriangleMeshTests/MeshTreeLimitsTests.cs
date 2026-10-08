using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.TriangleMeshTests;

public class MeshTreeLimitsTests
{
    private const int BoxTriangleCount = 12;

    private static MeshTriangles Cube() => BoxMesh.CreateTriangles(TestMeshes.UnitCube);

    [Fact]
    public void AMeshWithMoreTrianglesThanTheIndexLimitIsNotIndexed() =>
        Assert.Null(MeshTriangleTree.Build(Cube(), new MeshTreeLimits(BoxTriangleCount - 1, BoxTriangleCount)));

    [Fact]
    public void AMeshAtTheIndexLimitIsIndexed() =>
        Assert.NotNull(MeshTriangleTree.Build(Cube(), new MeshTreeLimits(BoxTriangleCount, BoxTriangleCount)));

    [Fact]
    public void AMeshWithMoreTrianglesThanTheClosednessLimitCountsAsOpen() =>
        Assert.False(MeshTriangleTree.Build(Cube(), new MeshTreeLimits(BoxTriangleCount, BoxTriangleCount - 1))!.IsClosed);

    [Fact]
    public void AClosedMeshAtTheClosednessLimitIsClosed() =>
        Assert.True(MeshTriangleTree.Build(Cube(), new MeshTreeLimits(BoxTriangleCount, BoxTriangleCount))!.IsClosed);
}
