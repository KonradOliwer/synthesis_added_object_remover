namespace AddedObjectRemover;

/// <param name="BodyMeshSetsMeasured">Distinct sets of body meshes whose bounds were measured.</param>
internal readonly record struct NpcBodyPerf(NpcBodyCacheStats Cache, int BodyMeshSetsMeasured);

/// <summary>
/// Reads the shared caches' statistics for the log. They depend on thread timing (what was built
/// or evicted first), so they never go into a step's result.
/// </summary>
internal interface IPerfProbe
{
    TriangleTreeStats Triangles();

    NpcBodyPerf Bodies();
}
