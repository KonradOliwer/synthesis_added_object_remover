namespace AddedObjectRemover;

internal readonly record struct MeshSupporter(Supporter Supporter, PlacedTransform Transform, string MeshPath);

/// <summary>
/// Everything with a mesh that may hold up an Anchoring candidate: target objects whose oriented
/// box comes within the touch distance of the candidate's (removed or not), and solids doing the
/// same. Thread-safe.
/// </summary>
internal sealed class AnchoringSupporterFinder(
    IReadOnlyList<TargetObject> targets,
    TouchSearch search,
    ISolids solids,
    ShapeCatalog shapes,
    float touchDistance)
{
    /// <returns>Target supporters in neighbor order, then placed supporters in id order.</returns>
    public List<MeshSupporter> FindMeshSupporters(int candidate, SpatialQueryScratch scratch)
    {
        var found = FindTargetSupporters(candidate);
        AddPlacedSupporters(candidate, scratch, found);
        return found;
    }

    private List<MeshSupporter> FindTargetSupporters(int candidate) =>
        search.CandidateFinder.FindNeighbors(candidate)
            .Select(neighbor => new MeshSupporter(Supporter.Target(neighbor), targets[neighbor].Transform, search.MeshPaths.Get(neighbor)))
            .ToList();

    private void AddPlacedSupporters(int candidate, SpatialQueryScratch scratch, List<MeshSupporter> found)
    {
        var candidateBox = search.CandidateFinder.BoxOf(candidate);
        solids.Overlapping(targets[candidate].SpaceKey, candidateBox.WorldAabb(touchDistance), scratch, scratch.Others);
        foreach (var id in scratch.Others)
        {
            var placed = solids.Get(id);
            if (shapes.GetMeshPath(placed.Base) is not { } meshPath) continue;

            var transform = placed.Transform;
            var placedBox = OrientedBox.FromLocal(shapes.GetLocalBox(placed.Base), transform);
            if (!candidateBox.Intersects(placedBox, touchDistance)) continue;

            found.Add(new MeshSupporter(Supporter.Placed(id), transform, meshPath));
        }
    }
}
