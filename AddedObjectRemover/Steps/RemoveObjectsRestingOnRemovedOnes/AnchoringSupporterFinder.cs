using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Everything with a mesh that may hold up an Anchoring candidate: target objects whose oriented
/// box comes within the touch distance of the candidate's (removed or not), and objects of any plugin doing the
/// same. Thread-safe.
/// </summary>
internal sealed class AnchoringSupporterFinder(
    IReadOnlyList<TargetObject> targets,
    TouchSearch search,
    IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
    IBaseObjectShapes shapes,
    float touchDistance)
{
    /// <returns>Target supporters in neighbor order, then placed supporters in id order.</returns>
    public List<MeshSupporter> FindMeshSupporters(int candidate, ObjectQueryScratch scratch)
    {
        var found = FindTargetSupporters(candidate);
        AddPlacedSupporters(candidate, scratch, found);
        return found;
    }

    private List<MeshSupporter> FindTargetSupporters(int candidate) =>
        search.CandidateFinder.FindNeighbors(candidate)
            .Select(neighbor => new MeshSupporter(Supporter.Target(neighbor), targets[neighbor].Transform, search.MeshPaths.Get(neighbor)))
            .ToList();

    private void AddPlacedSupporters(int candidate, ObjectQueryScratch scratch, List<MeshSupporter> found)
    {
        var candidateBox = search.CandidateFinder.BoxOf(candidate);
        objectsOfAnyPlugin.Touching(targets[candidate].SpaceKey, candidateBox, touchDistance, scratch, scratch.Others);
        foreach (var id in scratch.Others)
        {
            var placed = objectsOfAnyPlugin.Get(id);
            if (shapes.Of(placed.Base).MeshPath is not { } meshPath) continue;

            found.Add(new MeshSupporter(Supporter.Placed(id), placed.Transform, meshPath));
        }
    }
}
