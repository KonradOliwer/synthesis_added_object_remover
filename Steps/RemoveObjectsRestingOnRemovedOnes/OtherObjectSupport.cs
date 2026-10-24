using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The support of an object in a world: its contact points with what may hold it up (<see cref="SurfaceContacts"/>), each
/// point's weight split among the supporters it touches. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class OtherObjectSupport(SurfaceContacts surfaces, OtherModsObjectRules rules, float touchGap)
{
    /// <param name="other">A judged object, where it stands.</param>
    public CandidateContacts FindContacts(OtherObject other, SupportWorld world, OtherObjectQueries queries) =>
        FindContacts(rules.MeshPathOf(other), other.SpaceKey, other.Transform, rules.BoxOf(other, other.Transform), Supporter.Placed(other.Id), world, queries);

    /// <param name="target">A visible target object with a mesh, where it stands.</param>
    public CandidateContacts FindContacts(TargetObject target, SupportWorld world, OtherObjectQueries queries) =>
        FindContacts(rules.MeshPathOf(target), target.SpaceKey, target.Transform, rules.BoxOf(target), Supporter.Target(target.Id.Index), world, queries);

    private CandidateContacts FindContacts(
        string meshPath, RecordKey space, PlacedTransform transform, OrientedBox box, Supporter self, SupportWorld world, OtherObjectQueries queries)
    {
        var samples = surfaces.SampleSurface(meshPath);
        var supporters = world.FindSupporters(space, box.WorldAabb(touchGap), self, queries);
        var hits = surfaces.FindHits(samples.Points, space, transform, supporters, queries.Query.Triangles);
        return SupportContactFinder.SplitWeightsAmongSupporters(hits, samples.Weights);
    }
}
