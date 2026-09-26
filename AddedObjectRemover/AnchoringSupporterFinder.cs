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
    public List<MeshSupporter> FindMeshSupporters(int candidate)
    {
        var found = FindTargetSupporters(candidate);
        found.AddRange(FindPlacedSupporters(candidate));
        return found;
    }

    private List<MeshSupporter> FindTargetSupporters(int candidate) =>
        search.CandidateFinder.FindNeighbors(candidate)
            .Select(neighbor => new MeshSupporter(Supporter.Target(neighbor), targets[neighbor].Transform, search.MeshPaths.Get(neighbor)))
            .ToList();

    private IEnumerable<MeshSupporter> FindPlacedSupporters(int candidate)
    {
        var index = supporters.GetSpace(targets[candidate].SpaceKey);
        var candidateBox = search.CandidateFinder.BoxOf(candidate);
        var slots = new List<int>();
        index.Grid.Collect(candidateBox.WorldAabb(touchDistance + OtherObjectIndex.RawPositionSearchMargin), slots);
        foreach (var slot in slots.Distinct().Order())
        {
            if (!index.IsVisible(slot)) continue;
            var placed = index[slot];
            if (shapes.GetMeshPath(placed.Base) is not { } meshPath) continue;

            var transform = placed.Transform;
            var placedBox = OrientedBox.FromLocal(shapes.GetLocalBox(placed.Base), transform);
            if (!candidateBox.Intersects(placedBox, touchDistance)) continue;

            yield return new MeshSupporter(Supporter.Placed(slot), transform, meshPath);
        }
    }
}
