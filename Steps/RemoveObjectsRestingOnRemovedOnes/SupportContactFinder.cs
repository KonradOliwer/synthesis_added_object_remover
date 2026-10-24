using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a target object with everything that may hold it up (<see cref="SupporterFinder"/>, and terrain), found
/// by <see cref="SurfaceContacts"/>. A contact point touching several supporters splits its weight equally among them.
/// Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SupportContactFinder(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    SupporterFinder supporterFinder,
    SurfaceContacts surfaces)
{
    /// <param name="Hits">Per surface sample: whether it is in contact with the supporter.</param>
    internal readonly record struct SupporterHits(Supporter Supporter, bool[] Hits);

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
        var hits = surfaces.FindHits(samples.Points, target.SpaceKey, target.Transform, supporterFinder.FindMeshSupporters(candidate, scratch), scratch.Triangles);
        var contacts = SplitWeightsAmongSupporters(hits, samples.Weights);
        return new FoundContacts(contacts, ZeroWeightDiagnosis.Of(candidate, meshPaths.Get(candidate), samples, hits, contacts));
    }

    internal static CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, float[] weights)
    {
        var supportersPerPoint = CountSupportersPerPoint(hitsBySupporter, weights.Length);
        var bySupporter = new List<SupporterWeight>();
        foreach (var (supporter, hits) in hitsBySupporter)
        {
            var weight = WeightHeldBy(hits, weights, supportersPerPoint);
            if (weight > 0) bySupporter.Add(new SupporterWeight(supporter, weight));
        }

        var contactPoints = supportersPerPoint.Count(count => count > 0);
        var totalWeight = bySupporter.Sum(entry => entry.Weight);
        return new CandidateContacts(contactPoints, totalWeight, bySupporter);
    }

    internal static int[] CountSupportersPerPoint(IReadOnlyList<SupporterHits> hitsBySupporter, int pointCount)
    {
        var supportersPerPoint = new int[pointCount];
        foreach (var (_, hits) in hitsBySupporter)
        {
            for (var i = 0; i < pointCount; i++)
            {
                if (hits[i]) supportersPerPoint[i]++;
            }
        }
        return supportersPerPoint;
    }

    /// <returns>The supporter's weight before supporters without a weight above 0 are left out.</returns>
    internal static float WeightHeldBy(bool[] hits, float[] weights, int[] supportersPerPoint)
    {
        var weight = 0f;
        for (var i = 0; i < weights.Length; i++)
        {
            if (hits[i]) weight += weights[i] / supportersPerPoint[i];
        }
        return weight;
    }
}
