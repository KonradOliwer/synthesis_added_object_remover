using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class TriangleStoreLimitTests
{
    private const long ResidentLimitBytes = 1L << 20;

    /// <summary>
    /// One heavy tree is 36% of the limit, so three exceed it and two stay below the eviction
    /// target of 75%. Only the vertex array is oversized (and never touched), so the geometry
    /// builds in no time and is shared by every mesh.
    /// </summary>
    private const long HeavyTreeBytes = ResidentLimitBytes / 100 * 36;

    private const int BytesPerVertex = 12;
    private const int PaddingVertices = (int)(HeavyTreeBytes / BytesPerVertex);

    private static readonly NifGeometry HeavyGeometry = CreateHeavyGeometry();

    private static NifGeometry CreateHeavyGeometry()
    {
        var box = BoxMesh.CreateGeometry(TestMeshes.UnitCube);
        var vertices = new Vector3[PaddingVertices];
        box.Vertices.CopyTo(vertices, 0);
        return new NifGeometry(box.Min, box.Max, vertices, box.Indices, box.PartFirstTriangles);
    }

    private static TriangleStore CreateHeavyStore(out Dictionary<string, int> reads)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        reads = counts;
        return new TriangleStore(
            path =>
            {
                lock (counts) counts[path] = counts.GetValueOrDefault(path) + 1;
                return HeavyGeometry;
            },
            ResidentLimitBytes);
    }

    [Fact]
    public void ALeasedTreeSurvivesEvictionAndIdleOnesGoInsteadWhileOverTheLimit()
    {
        var store = CreateHeavyStore(out var reads);
        using var leasedA = store.Acquire("a.nif");
        store.Acquire("b.nif").Dispose();
        using var leasedC = store.Acquire("c.nif");

        Assert.Equal(1, store.GetStats().Evicted);
        Assert.NotNull(leasedA.Tree);

        using var againA = store.Acquire("a.nif");
        Assert.Same(leasedA.Tree, againA.Tree);
        Assert.Equal(1, reads["a.nif"]);
        Assert.NotNull(leasedC.Tree);
    }

    [Fact]
    public void TheLeastRecentlyUsedIdleTreeIsEvictedFirstAndStopsAtTheTarget()
    {
        var store = CreateHeavyStore(out var reads);
        store.Acquire("a.nif").Dispose();
        store.Acquire("b.nif").Dispose();
        using var leasedC = store.Acquire("c.nif");

        Assert.Equal(1, store.GetStats().Evicted);
        store.Acquire("b.nif").Dispose();
        Assert.Equal(1, reads["b.nif"]);
        store.Acquire("a.nif").Dispose();
        Assert.Equal(2, reads["a.nif"]);
    }

    [Fact]
    public void ARebuiltMeshCountsAsBuiltAgainAndRebuilt()
    {
        var store = CreateHeavyStore(out _);
        store.Acquire("a.nif").Dispose();
        store.Acquire("b.nif").Dispose();
        using var leasedC = store.Acquire("c.nif");
        Assert.Equal(0, store.GetStats().Rebuilt);

        store.Acquire("A.nif").Dispose();

        var stats = store.GetStats();
        Assert.Equal(4, stats.Built);
        Assert.Equal(1, stats.Rebuilt);
    }

    [Fact]
    public void PeaksKeepTheirValueAfterEviction()
    {
        var store = CreateHeavyStore(out _);
        store.Acquire("a.nif").Dispose();
        store.Acquire("b.nif").Dispose();
        using var leasedC = store.Acquire("c.nif");

        var stats = store.GetStats();
        Assert.Equal(1, stats.Evicted);
        Assert.Equal(3, stats.PeakResidentMeshes);
        Assert.True(stats.PeakResidentBytes > ResidentLimitBytes);
    }

    [Fact]
    public void AMeshAboveTheTriangleLimitHasNoTreeIsCountedOnceAndNotReadAgain()
    {
        var reads = 0;
        var indices = new int[3 * (MeshTriangleTree.MaxTriangles + 1)];
        var geometry = new NifGeometry(Vector3.Zero, Vector3.One, [Vector3.Zero], indices, [0]);
        var store = new TriangleStore(_ =>
        {
            reads++;
            return geometry;
        });

        using (var first = store.Acquire("huge.nif")) Assert.Null(first.Tree);
        using (var second = store.Acquire("huge.nif")) Assert.Null(second.Tree);

        var stats = store.GetStats();
        Assert.Equal(1, stats.TooLarge);
        Assert.Equal(0, stats.Built);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ConcurrentBuildsOfLargeMeshesAllCompleteAndBuildEachOnce()
    {
        var indices = new int[3 * 25_000];
        var geometry = new NifGeometry(Vector3.Zero, Vector3.One, [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], indices, [0]);
        var reads = 0;
        var store = new TriangleStore(_ =>
        {
            Interlocked.Increment(ref reads);
            return geometry;
        });

        Parallel.For(0, 12, new ParallelOptions { MaxDegreeOfParallelism = 12 }, i =>
        {
            using var lease = store.Acquire($"large{i}.nif");
            Assert.NotNull(lease.Tree);
        });

        Assert.Equal(12, reads);
        Assert.Equal(12, store.GetStats().Built);
    }
}
