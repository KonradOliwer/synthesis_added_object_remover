namespace AddedObjectRemover;

/// <summary>
/// Mesh trees leased from <see cref="ITriangleMeshes"/> on first use and held until <see cref="Release"/>, so a mesh used again
/// takes no new lease and cannot be evicted and built again in between. For one thread.
/// </summary>
public sealed class HeldMeshes(ITriangleMeshes meshes)
{
    private readonly Dictionary<string, Lease<MeshTriangleTree>> _leases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mesh's tree, or null when it has no usable triangles or is too large.</summary>
    public MeshTriangleTree? Get(string meshPath)
    {
        if (!_leases.TryGetValue(meshPath, out var lease))
        {
            lease = meshes.Acquire(meshPath);
            _leases.Add(meshPath, lease);
        }
        return lease.Value;
    }

    public void Release()
    {
        foreach (var lease in _leases.Values) lease.Dispose();
        _leases.Clear();
    }
}
