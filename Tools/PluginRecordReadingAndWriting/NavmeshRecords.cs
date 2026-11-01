namespace AddedObjectRemover;

/// <summary>The winning navmesh records of each bucket, decoded to triangles on request.</summary>
internal sealed class NavmeshRecords
{
    private readonly Dictionary<NavmeshBucket, CellNavmesh[]> _navmeshes;

    public NavmeshRecords(IReadOnlyDictionary<RecordKey, List<CellNavmesh>> navmeshesBySpace)
    {
        _navmeshes = navmeshesBySpace
            .SelectMany(space => space.Value.Select(navmesh => (Bucket: new NavmeshBucket(space.Key, navmesh.Grid), Navmesh: navmesh)))
            .GroupBy(entry => entry.Bucket, entry => entry.Navmesh)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    /// <returns>Null where the bucket has no navmesh.</returns>
    public NavmeshWorldTriangle[]? ReadTriangles(NavmeshBucket bucket) =>
        _navmeshes.TryGetValue(bucket, out var navmeshes) ? navmeshes.SelectMany(TrianglesOf).ToArray() : null;

    private static IEnumerable<NavmeshWorldTriangle> TrianglesOf(CellNavmesh navmesh) =>
        NavmeshTriangles.Read(navmesh.Data).Select(triangle => new NavmeshWorldTriangle(navmesh.Navmesh, triangle));
}
