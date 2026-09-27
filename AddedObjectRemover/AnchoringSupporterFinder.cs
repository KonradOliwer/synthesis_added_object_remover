namespace AddedObjectRemover;

internal readonly record struct MeshSupporter(Supporter Supporter, PlacedTransform Transform, string MeshPath);

/// <summary>
/// Everything with a mesh that may hold up an Anchoring candidate: target objects whose oriented
/// box comes within the touch distance of the candidate's (removed or not), and visible placed
/// objects of any plugin doing the same (<see cref="SupporterIndex"/>). Thread-safe.
/// </summary>
internal sealed class AnchoringSupporterFinder(
    IReadOnlyList<TargetObject> targets,
    TouchSearch search,
    SupporterIndex supporters,
    BaseObjectShapeProvider shapes,
    float touchDistance)
{
    /// <returns>Target supporters in neighbor order, then placed supporters in index order.</returns>
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
        var index = supporters.GetSpace(targets[candidate].SpaceKey);
        var candidateBox = search.CandidateFinder.BoxOf(candidate);
        index.Bounds.CollectCandidates(candidateBox.WorldAabb(touchDistance), scratch.Slots, scratch.Candidates);
        foreach (var slot in scratch.Candidates)
        {
            if (!index.IsVisible(slot)) continue;
            var placed = index[slot];
            if (shapes.GetMeshPath(placed.Base) is not { } meshPath) continue;

            var transform = placed.Transform;
            var placedBox = OrientedBox.FromLocal(shapes.GetLocalBox(placed.Base), transform);
            if (!candidateBox.Intersects(placedBox, touchDistance)) continue;

            found.Add(new MeshSupporter(Supporter.Placed(slot), transform, meshPath));
        }
    }
}
