using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The support of an object in a world: its contact points with what may hold it up (<see cref="SurfaceContacts"/>), each
/// point's weight split among the supporters it touches. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class OtherObjectSupport(SurfaceContacts surfaces, float touchGap)
{
    public CandidateContacts FindContacts(StandingObject standing, SupportWorld world, OtherObjectQueries queries)
    {
        var samples = surfaces.SampleSurface(standing.MeshPath);
        var supporters = world.FindSupporters(standing.Space, standing.Box.WorldAabb(touchGap), standing.Self, queries);
        var hits = surfaces.FindHits(samples.Points, standing.Space, standing.Transform, supporters, queries.Query.Triangles);
        return SupportContactFinder.SplitWeightsAmongSupporters(hits, samples.Weights);
    }
}
