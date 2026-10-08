using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class TriangleStoreTests
{
    [Fact]
    public void ConcurrentUsesBuildEachMeshOnce()
    {
        var reads = 0;
        var cache = new TriangleStore(_ =>
        {
            Interlocked.Increment(ref reads);
            return BoxMesh.CreateTriangles(TestMeshes.UnitCube);
        });

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            using var lease = cache.Acquire(@"meshes\box.nif");
            Assert.NotNull(lease.Value);
        });

        Assert.Equal(1, reads);
        Assert.Equal(1, cache.GetStats().Built);
    }

    [Fact]
    public void PathsDifferingInCaseShareOneTree()
    {
        var cache = new TriangleStore(_ => BoxMesh.CreateTriangles(TestMeshes.UnitCube));
        using var first = cache.Acquire(@"Meshes\Box.nif");
        using var second = cache.Acquire(@"meshes\box.nif");
        Assert.Same(first.Value, second.Value);
    }

    [Fact]
    public void MeshWithoutTrianglesHasNoTree()
    {
        var cache = new TriangleStore(_ => null);
        using var lease = cache.Acquire("missing.nif");
        Assert.Null(lease.Value);
    }

    [Fact]
    public void FailedBuildIsRetriedOnTheNextUse()
    {
        var reads = 0;
        var cache = new TriangleStore(_ =>
        {
            reads++;
            if (reads <= 2) throw new IOException("unreadable");
            return BoxMesh.CreateTriangles(TestMeshes.UnitCube);
        });

        Assert.Throws<IOException>(() => cache.Acquire("flaky.nif"));
        Assert.Throws<IOException>(() => cache.Acquire("flaky.nif"));
        using var lease = cache.Acquire("flaky.nif");

        Assert.NotNull(lease.Value);
        Assert.Equal(3, reads);
        Assert.Equal(1, cache.GetStats().Built);
    }

    [Fact]
    public void DisposingADefaultLeaseDoesNothing()
    {
        var exception = Record.Exception(() => default(Lease<MeshTriangleTree>).Dispose());
        Assert.Null(exception);
    }
}
