namespace AddedObjectRemover;

public readonly record struct TriangleStoreStats(int Built, int TooLarge, long Triangles);

/// <summary>Cache C6: triangle trees of meshes by path, built on first use and shared by all callers.</summary>
public interface ITriangleMeshes
{
    /// <summary>The tree (null when the mesh has no usable triangles or is too large) stays valid until the lease is disposed.</summary>
    Lease<MeshTriangleTree> Acquire(string meshPath);

    /// <summary>Counted from the cache contents, so evicted trees are still counted.</summary>
    TriangleStoreStats GetStats();
}
