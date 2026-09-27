using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class TriangleTreeCacheTests
{
    [Fact]
    public void ConcurrentUsesBuildEachMeshOnce()
    {
        var reads = 0;
        var cache = new TriangleTreeCache(_ =>
        {
            Interlocked.Increment(ref reads);
            return BoxMesh.CreateGeometry(TestMeshes.UnitCube);
        });

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            using var lease = cache.Acquire(@"meshes\box.nif");
            Assert.NotNull(lease.Tree);
        });

        Assert.Equal(1, reads);
        Assert.Equal(1, cache.GetStats().Built);
    }

    [Fact]
    public void PathsDifferingInCaseShareOneTree()
    {
        var cache = new TriangleTreeCache(_ => BoxMesh.CreateGeometry(TestMeshes.UnitCube));
        using var first = cache.Acquire(@"Meshes\Box.nif");
        using var second = cache.Acquire(@"meshes\box.nif");
        Assert.Same(first.Tree, second.Tree);
    }

    [Fact]
    public void MeshWithoutTrianglesHasNoTree()
    {
        var cache = new TriangleTreeCache(_ => null);
        using var lease = cache.Acquire("missing.nif");
        Assert.Null(lease.Tree);
    }

    [Fact]
    [Trait(KnownBug.Trait, "A6: a failed build is cached, so the mesh is never retried and its use count is never released")]
    public void FailedBuildIsNeverRetried()
    {
        var reads = 0;
        var cache = new TriangleTreeCache(_ =>
        {
            reads++;
            throw new IOException("unreadable");
        });

        Assert.Throws<IOException>(() => cache.Acquire("broken.nif"));
        Assert.Throws<IOException>(() => cache.Acquire("broken.nif"));
        Assert.Equal(1, reads);
    }

    [Fact]
    [Trait(KnownBug.Trait, "A6: disposing a default lease dereferences a null cache")]
    public void DefaultLeaseCannotBeDisposed() =>
        Assert.Throws<NullReferenceException>(() => default(TriangleTreeCache.Lease).Dispose());
}
