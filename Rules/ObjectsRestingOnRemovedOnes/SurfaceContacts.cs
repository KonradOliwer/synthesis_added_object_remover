using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a placed mesh with what may hold it up, the support rule of target objects and other mods' objects alike.
/// The mesh's surface is sampled, each sample weighted by its closeness to the mesh origin: most placed objects have their
/// origin where they rest, so contact there anchors more than contact far away. A sample is a contact point for each mesh supporter
/// whose surface it comes within the touch distance of or is enclosed by, and for terrain when it lies at or below terrain height
/// plus the touch distance (<see cref="SampleContacts"/>). Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SurfaceContacts(ITerrainHeights terrain, ITriangleMeshes cache, ISurfaceSamples surfaces) : ISupportContacts
{
    /// <summary>The number of points sampled on a mesh's surface (<see cref="ISurfaceSamples"/>).</summary>
    internal const int SamplesPerMesh = 4096;

    /// <summary>Distance from the origin, as a fraction of the bounds diagonal, at which a sample has half weight.</summary>
    internal const float OriginFalloffFraction = 0.25f;

    public WeightedSurfaceSamples SampleSurface(string meshPath) => surfaces.Of(meshPath) is { } surface ? Weigh(surface) : WeightedSurfaceSamples.Empty;

    public List<SupporterHits> FindHits(
        ImmutableArray<Vector3> points, RecordKey space, PlacedTransform transform, IEnumerable<MeshSupporter> supporters, float touchDistance, List<int> scratch)
    {
        var hits = supporters
            .Select(supporter => new SupporterHits(
                supporter.Supporter, SampleContacts.TouchingMesh(cache, supporter.MeshPath, supporter.Transform, points, transform, touchDistance, scratch)))
            .ToList();
        if (terrain.HasTerrain(space)) hits.Add(new SupporterHits(Supporter.Terrain, SampleContacts.OnTerrain(terrain, space, points, transform, touchDistance)));
        return hits;
    }

    public CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, ImmutableArray<float> weights) =>
        SupportContactWeights.SplitWeightsAmongSupporters(hitsBySupporter, weights);

    public bool ReachesThreshold(float lostShare, float threshold) => SupportContactWeights.ReachesThreshold(lostShare, threshold);

    internal static WeightedSurfaceSamples Weigh(SampledSurface surface)
    {
        var falloff = SampleWeights.FalloffOf(surface.Bounds, OriginFalloffFraction);
        return new WeightedSurfaceSamples(surface.Points, SampleWeights.ByOriginDistance(surface.Points, falloff), surface.Bounds, falloff);
    }
}
