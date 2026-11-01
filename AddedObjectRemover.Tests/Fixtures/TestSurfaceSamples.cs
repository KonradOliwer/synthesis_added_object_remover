namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestSurfaceSamples
{
    /// <summary>A surface sample store over <paramref name="triangles"/>, sampled as in a run.</summary>
    public static ISurfaceSamples Over(ITriangleMeshes triangles) => new SurfaceSampleStore(triangles, RestingObjects.SurfaceSamplesPerMesh);
}
