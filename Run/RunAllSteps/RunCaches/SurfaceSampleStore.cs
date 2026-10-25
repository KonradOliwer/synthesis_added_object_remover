using System.Collections.Immutable;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// Thread-safe store of <see cref="SampledSurface"/> per mesh path, sampled from the run's triangle trees on first use and kept
/// for the run, also after the tree is evicted.
/// </summary>
internal sealed class SurfaceSampleStore(ITriangleMeshes triangles, int samplesPerMesh) : ISurfaceSamples
{
    // Sampling is deterministic, so a surface two threads sample at once is the same either way. A mesh without a tree is kept
    // as null; a tree build that throws is not kept, so the next ask tries again.
    private readonly ComputedOncePerKey<string, SampledSurface?> _surfaces = new(Publication.FirstWriteWins, StringComparer.OrdinalIgnoreCase);

    public SampledSurface? Of(string meshPath) => _surfaces.Get(meshPath, () => Sample(meshPath));

    private SampledSurface? Sample(string meshPath)
    {
        using var lease = triangles.Acquire(meshPath);
        return lease.Value is { } tree ? new SampledSurface(SurfaceSampler.Sample(tree, samplesPerMesh).ToImmutableArray(), tree.Bounds) : null;
    }
}
