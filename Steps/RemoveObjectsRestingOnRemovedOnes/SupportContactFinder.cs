using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a target object with everything that may hold it up (<see cref="SupporterFinder"/>, and terrain), found
/// by <see cref="SurfaceContacts"/> within the touch gap, their weights split among the supporters (<see cref="SupportContactWeights"/>).
/// Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SupportContactFinder(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    SupporterFinder supporterFinder,
    SurfaceContacts surfaces,
    float touchGap)
{
    /// <param name="ZeroWeight">Set when the contacts have contact points but no support weight.</param>
    internal readonly record struct FoundContacts(CandidateContacts Contacts, ZeroWeightDiagnostic? ZeroWeight);

    /// <param name="candidates">Indexes of target objects with a mesh.</param>
    /// <returns>One per candidate, in the given order.</returns>
    public FoundContacts[] FindContactsOfAll(IReadOnlyList<int> candidates, Execution execution) =>
        ParallelMap.Run(
            execution,
            candidates.Count,
            () => new ObjectQueryScratch(),
            (k, scratch) => FindContacts(candidates[k], scratch),
            ParallelMap.AutomaticRangeSize);

    /// <param name="candidate">Index of a target object with a mesh.</param>
    /// <param name="scratch">The calling thread's buffers.</param>
    private FoundContacts FindContacts(int candidate, ObjectQueryScratch scratch)
    {
        var target = targets[candidate];
        var samples = surfaces.SampleSurface(meshPaths.Get(candidate));
        var hits = surfaces.FindHits(samples.Points, target.SpaceKey, target.Transform, supporterFinder.FindMeshSupporters(candidate, scratch), touchGap, scratch.Triangles);
        var contacts = SupportContactWeights.SplitWeightsAmongSupporters(hits, samples.Weights);
        return new FoundContacts(contacts, ZeroWeightDiagnosis.Of(candidate, meshPaths.Get(candidate), samples, hits, contacts));
    }
}
