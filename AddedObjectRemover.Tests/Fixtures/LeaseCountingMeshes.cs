namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Mesh trees from the resident mesh trees, with a count of the leases taken and of those not yet ended.</summary>
internal sealed class LeaseCountingMeshes(Func<string, MeshTriangles?> readTriangles) : ITriangleMeshes, ILeaseOwner<MeshTriangleTree>
{
    private readonly ResidentMeshTrees _store = new(readTriangles);
    private int _acquired;
    private int _open;

    public int Acquired => Volatile.Read(ref _acquired);

    public int Open => Volatile.Read(ref _open);

    public Lease<MeshTriangleTree> Acquire(string meshPath)
    {
        using var stored = _store.Acquire(meshPath);
        Interlocked.Increment(ref _acquired);
        Interlocked.Increment(ref _open);
        return new Lease<MeshTriangleTree>(stored.Value, this, new StoreSlot<MeshTriangleTree>());
    }

    public ResidentMeshTreesStats GetStats() => _store.GetStats();

    void ILeaseOwner<MeshTriangleTree>.EndUse(StoreSlot<MeshTriangleTree> slot) => Interlocked.Decrement(ref _open);
}
