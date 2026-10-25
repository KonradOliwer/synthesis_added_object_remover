using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Meshes;

public class SurfaceSampleStoreTests
{
    [Fact]
    public void RepeatedAsksGetTheSameSamplesWhateverTheCaseOfThePath()
    {
        var store = TestSurfaceSamples.Over(new TriangleStore(_ => BoxMesh.CreateTriangles(TestMeshes.UnitCube)));

        var first = store.Of(@"Meshes\Box.nif");

        Assert.NotNull(first);
        Assert.Same(first, store.Of(@"Meshes\Box.nif"));
        Assert.Same(first, store.Of(@"meshes\box.nif"));
    }

    [Fact]
    public void MeshWithoutTrianglesHasNoSamples()
    {
        var store = TestSurfaceSamples.Over(new TriangleStore(_ => null));

        Assert.Null(store.Of("missing.nif"));
        Assert.Null(store.Of("missing.nif"));
    }

    [Fact]
    public void FailedSamplingIsRetriedOnTheNextAsk()
    {
        var reads = 0;
        var store = TestSurfaceSamples.Over(new TriangleStore(_ =>
        {
            reads++;
            if (reads == 1) throw new IOException("unreadable");
            return BoxMesh.CreateTriangles(TestMeshes.UnitCube);
        }));

        Assert.Throws<IOException>(() => store.Of("flaky.nif"));

        Assert.NotNull(store.Of("flaky.nif"));
        Assert.Equal(2, reads);
    }
}
