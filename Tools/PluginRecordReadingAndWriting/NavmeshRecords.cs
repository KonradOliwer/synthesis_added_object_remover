using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>The winning navmesh records of each bucket, decoded to triangles on request.</summary>
internal sealed class NavmeshRecords
{
    private readonly Dictionary<NavmeshBucket, INavigationMeshDataGetter[]> _navmeshes;

    public NavmeshRecords(IReadOnlyDictionary<RecordKey, List<CellNavmesh>> navmeshesBySpace)
    {
        _navmeshes = navmeshesBySpace
            .SelectMany(space => space.Value.Select(navmesh => (Bucket: new NavmeshBucket(space.Key, navmesh.Grid), navmesh.Data)))
            .GroupBy(entry => entry.Bucket, entry => entry.Data)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    /// <returns>Null where the bucket has no navmesh.</returns>
    public MeshTriangle[]? ReadTriangles(NavmeshBucket bucket) =>
        _navmeshes.TryGetValue(bucket, out var navmeshes) ? navmeshes.SelectMany(NavmeshTriangles.Read).ToArray() : null;
}
