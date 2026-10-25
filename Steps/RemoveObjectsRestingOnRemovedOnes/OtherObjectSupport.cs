using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.SupportContactFinder;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>Which samples of an object's surface touch each supporter in a world, and the samples' weights.</summary>
/// <param name="BySupporter">In the order the world gives the supporters.</param>
internal readonly record struct SupportHits(List<SupporterHits> BySupporter, float[] Weights)
{
    public CandidateContacts Contacts() => SplitWeightsAmongSupporters(BySupporter, Weights);

    /// <summary>The contacts in the same world with the given target objects taken out.</summary>
    /// <remarks>
    /// Equal to finding them again in that world: each supporter's hits depend only on it and the object, and taking target
    /// objects out leaves the others in their order, so the weights add up in the same order.
    /// </remarks>
    public CandidateContacts ContactsWithout(IReadOnlyCollection<TargetId> takenOut) =>
        SplitWeightsAmongSupporters([.. BySupporter.Where(hits => !takenOut.Any(target => hits.Supporter == Supporter.Target(target.Index)))], Weights);
}

/// <summary>
/// The support of an object in a world: its contact points with what may hold it up (<see cref="SurfaceContacts"/>), each
/// point's weight split among the supporters it touches. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class OtherObjectSupport(SurfaceContacts surfaces, float touchGap)
{
    public CandidateContacts FindContacts(StandingObject standing, SupportWorld world, OtherObjectQueries queries) => FindHits(standing, world, queries).Contacts();

    public SupportHits FindHits(StandingObject standing, SupportWorld world, OtherObjectQueries queries)
    {
        var samples = surfaces.SampleSurface(standing.MeshPath);
        var supporters = world.FindSupporters(standing.Space, standing.Box.WorldAabb(touchGap), standing.Self, queries);
        return new SupportHits(surfaces.FindHits(samples.Points, standing.Space, standing.Transform, supporters, queries.Query.Triangles), samples.Weights);
    }
}
