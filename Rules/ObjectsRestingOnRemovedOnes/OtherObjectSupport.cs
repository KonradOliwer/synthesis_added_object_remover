using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>Which samples of an object's surface touch each supporter in a world, and the samples' weights.</summary>
/// <param name="BySupporter">In the order the world gives the supporters.</param>
internal readonly record struct SupportHits(List<SupporterHits> BySupporter, ImmutableArray<float> Weights);

/// <summary>
/// The support of an object in a world: its contact points with what may hold it up (<see cref="ISupportContacts"/>), each
/// point's weight split among the supporters it touches. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class OtherObjectSupport(ISupportContacts contacts, float touchGap)
{
    public CandidateContacts FindContacts(StandingObject standing, SupportWorld world, OtherObjectQueries queries) => ContactsOf(FindHits(standing, world, queries));

    public SupportHits FindHits(StandingObject standing, SupportWorld world, OtherObjectQueries queries)
    {
        var samples = contacts.SampleSurface(standing.MeshPath);
        var supporters = world.FindSupporters(standing.Space, standing.Box.WorldAabb(touchGap), standing.Self, queries);
        return new SupportHits(contacts.FindHits(samples.Points, standing.Space, standing.Transform, supporters, world.Terrain, touchGap, queries.Query.Triangles), samples.Weights);
    }

    public CandidateContacts ContactsOf(SupportHits hits) => contacts.SplitWeightsAmongSupporters(hits.BySupporter, hits.Weights);

    /// <summary>The contacts in the same world with the given target objects taken out.</summary>
    /// <remarks>
    /// Equal to finding them again in that world: each supporter's hits depend only on it and the object, and taking target
    /// objects out leaves the others in their order, so the weights add up in the same order.
    /// </remarks>
    public CandidateContacts ContactsWithout(SupportHits hits, IReadOnlyCollection<TargetId> takenOut) =>
        contacts.SplitWeightsAmongSupporters([.. hits.BySupporter.Where(entry => !takenOut.Any(target => entry.Supporter == Supporter.Target(target.Index)))], hits.Weights);

    public bool ReachesThreshold(float lostShare, float threshold) => contacts.ReachesThreshold(lostShare, threshold);
}
