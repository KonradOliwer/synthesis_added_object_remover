using AddedObjectRemover.Caches.RunCaches.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>What a mesh gave when it was first built; kept for the statistics after its tree is evicted.</summary>
/// <param name="Triangles">Zero when the mesh is too large to be used.</param>
internal readonly record struct BuiltMesh(int Triangles, bool TooLarge);

/// <summary>
/// Thread-safe store of <see cref="MeshTriangleTree"/> per mesh path. A mesh is built on its first
/// use and stays resident until the resident estimate exceeds the maximum resident bytes; then
/// the least recently used meshes that are not in use are dropped (and rebuilt if needed again).
/// Builds of large meshes are limited to a few at a time, because reading and indexing
/// temporarily needs several times the finished tree's memory.
/// </summary>
internal sealed class TriangleStore : ITriangleMeshes
{
    public const long DefaultMaxResidentBytes = 1L << 30;

    private const int LargeMeshTriangles = 20_000;
    private const int MaxConcurrentLargeBuilds = 4;

    private readonly Func<string, MeshTriangles?> _readTriangles;
    private readonly EvictingLeasedStore<string, MeshTriangleTree> _trees;
    private readonly SemaphoreSlim _largeBuilds = new(MaxConcurrentLargeBuilds);

    private readonly ComputedOncePerKey<string, BuiltMesh> _builtMeshes = new(Publication.BuiltOnce, StringComparer.OrdinalIgnoreCase);

    /// <param name="readTriangles">Gives the triangles of a mesh path; null when the mesh has none.</param>
    /// <param name="maxResidentBytes">The resident estimate above which idle trees are evicted, down to three quarters of it.</param>
    public TriangleStore(Func<string, MeshTriangles?> readTriangles, long maxResidentBytes = DefaultMaxResidentBytes)
    {
        _readTriangles = readTriangles;
        _trees = new EvictingLeasedStore<string, MeshTriangleTree>(
            Build, tree => tree.EstimatedBytes, maxResidentBytes, maxResidentBytes / 4 * 3, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tree (null when the mesh has no usable triangles or is too large) stays resident at
    /// least until the returned lease is disposed. When the build throws, the exception passes
    /// through and the mesh is built again on its next use.
    /// </summary>
    public Lease<MeshTriangleTree> Acquire(string meshPath) => _trees.Acquire(meshPath);

    public TriangleStoreStats GetStats()
    {
        var meshes = _builtMeshes.Contents();
        return new TriangleStoreStats(
            meshes.Count(mesh => !mesh.TooLarge),
            meshes.Count(mesh => mesh.TooLarge),
            meshes.Sum(mesh => (long)mesh.Triangles));
    }

    private MeshTriangleTree? Build(string meshPath)
    {
        var tree = ReadAndIndexWithLargeMeshLimit(meshPath, out var triangles);
        if (triangles is not { TriangleCount: > 0 }) return null;
        _builtMeshes.Get(meshPath, () => new BuiltMesh(tree?.TriangleCount ?? 0, TooLarge: tree == null));
        return tree;
    }

    /// <remarks>The mesh size is only known after reading, so a large mesh takes a build slot once its triangles are in memory.</remarks>
    private MeshTriangleTree? ReadAndIndexWithLargeMeshLimit(string meshPath, out MeshTriangles? triangles)
    {
        triangles = _readTriangles(meshPath);
        if (triangles is not { TriangleCount: > 0 }) return null;

        var large = triangles.TriangleCount > LargeMeshTriangles;
        if (large) _largeBuilds.Wait();
        try
        {
            return MeshTriangleTree.Build(triangles, MeshLimits.Tree);
        }
        finally
        {
            if (large) _largeBuilds.Release();
        }
    }
}
