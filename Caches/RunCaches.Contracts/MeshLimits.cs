namespace AddedObjectRemover.Caches.RunCaches.Contracts;

public static class MeshLimits
{
    public static readonly MeshTreeLimits Tree = new(MaxIndexedTriangles: 2_000_000, MaxClosednessTriangles: 200_000);
}
