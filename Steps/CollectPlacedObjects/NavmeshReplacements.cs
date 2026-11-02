using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// A navmesh the cleaned mod deletes is replaced by the navmeshes it adds in the deleted one's cell whose triangles overlap the
/// deleted one's earlier triangles seen from above.
/// </summary>
internal static class NavmeshReplacements
{
    /// <param name="navmeshes">The navmeshes the cleaned mod changed, in key order.</param>
    /// <returns>Each deleted navmesh with a replacement, with its replacements in key order.</returns>
    public static IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> Of(IReadOnlyList<NavmeshRecordFact> navmeshes)
    {
        var added = navmeshes
            .Where(navmesh => navmesh.Case == NavmeshCase.Added)
            .Select(navmesh => new AddedNavmesh(navmesh.Key, navmesh.Current.Cell, [.. navmesh.Current.WorldTriangles()]))
            .ToList();
        var replacements = new Dictionary<RecordKey, ImmutableArray<RecordKey>>();
        foreach (var navmesh in navmeshes)
        {
            if (navmesh is not { Case: NavmeshCase.Deleted, Earlier: { } earlier }) continue;
            var replacedBy = ReplacementsOf(earlier, added);
            if (!replacedBy.IsEmpty) replacements.Add(navmesh.Key, replacedBy);
        }
        return replacements;
    }

    /// <param name="added">In key order.</param>
    private static ImmutableArray<RecordKey> ReplacementsOf(NavmeshShape earlier, IEnumerable<AddedNavmesh> added)
    {
        var earlierTriangles = earlier.WorldTriangles().ToList();
        return
        [
            .. added
                .Where(navmesh => navmesh.Cell == earlier.Cell
                    && earlierTriangles.Any(triangle => navmesh.Triangles.Any(addedTriangle => TriangleFootprints.Overlap(triangle, addedTriangle))))
                .Select(navmesh => navmesh.Key),
        ];
    }

    /// <param name="Cell">The cell record whose navmesh list holds the cleaned mod's version.</param>
    /// <param name="Triangles">The cleaned mod's version's triangles in world space.</param>
    private sealed record AddedNavmesh(RecordKey Key, RecordKey Cell, ImmutableArray<MeshTriangle> Triangles);
}
