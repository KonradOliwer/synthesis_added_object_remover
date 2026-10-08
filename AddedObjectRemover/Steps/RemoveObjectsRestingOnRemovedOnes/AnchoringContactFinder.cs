using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a target object with everything that may hold it up
/// (<see cref="AnchoringSupporterFinder"/>, and terrain). The object's surface is sampled with
/// weights (<see cref="WeightedSurfaceSamples"/>); a sample is a contact point for each mesh
/// supporter whose surface it comes within the touch distance of or is enclosed by
/// (<see cref="MeshContact.SamplesTouching"/>), and for terrain when it lies at or below terrain height plus
/// the touch distance. A contact point touching several supporters splits its weight equally
/// among them. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class AnchoringContactFinder(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    AnchoringSupporterFinder supporterFinder,
    ITerrainHeights terrain,
    ITriangleMeshes cache,
    float touchDistance)
{
    /// <param name="Hits">Per surface sample: whether it is in contact with the supporter.</param>
    internal readonly record struct SupporterHits(Supporter Supporter, bool[] Hits);

    /// <param name="candidate">Index of a target object with a mesh.</param>
    /// <param name="scratch">The calling thread's buffers.</param>
    public CandidateContacts FindContacts(int candidate, ObjectQueryScratch scratch)
    {
        var target = targets[candidate];
        var samples = SampleSurface(candidate);
        var hits = supporterFinder.FindMeshSupporters(candidate, scratch)
            .Select(supporter => new SupporterHits(supporter.Supporter, FindSamplesTouchingMesh(samples.Points, target.Transform, supporter, scratch)))
            .ToList();
        if (terrain.HasTerrain(target.SpaceKey))
        {
            hits.Add(new SupporterHits(Supporter.Terrain, FindSamplesOnTerrain(samples.Points, target)));
        }
        return SplitWeightsAmongSupporters(hits, samples.Weights);
    }

    private WeightedSurfaceSamples SampleSurface(int candidate)
    {
        using var lease = cache.Acquire(meshPaths.Get(candidate));
        return lease.Value is { } tree ? WeightedSurfaceSamples.Create(tree) : WeightedSurfaceSamples.Empty;
    }

    /// <returns>Per sample: whether it is in contact with the supporter's mesh.</returns>
    private bool[] FindSamplesTouchingMesh(Vector3[] points, PlacedTransform candidateTransform, MeshSupporter supporter, ObjectQueryScratch scratch)
    {
        using var lease = cache.Acquire(supporter.MeshPath);
        return lease.Value is { } tree
            ? MeshContact.SamplesTouching(points, candidateTransform, tree, supporter.Transform, touchDistance, scratch.Triangles)
            : new bool[points.Length];
    }

    /// <returns>Per sample: whether it lies at or below terrain height plus the touch distance.</returns>
    private bool[] FindSamplesOnTerrain(Vector3[] points, TargetObject candidate)
    {
        var hits = new bool[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var world = candidate.Transform.ToWorld(points[i]);
            hits[i] = terrain.TryGetHeight(candidate.SpaceKey, new Vector2(world.X, world.Y), out var height)
                      && world.Z <= height + touchDistance;
        }
        return hits;
    }

    internal static CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, float[] weights)
    {
        var supportersPerPoint = new int[weights.Length];
        foreach (var (_, hits) in hitsBySupporter)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                if (hits[i]) supportersPerPoint[i]++;
            }
        }

        var bySupporter = new List<SupporterWeight>();
        foreach (var (supporter, hits) in hitsBySupporter)
        {
            var weight = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                if (hits[i]) weight += weights[i] / supportersPerPoint[i];
            }
            if (weight > 0) bySupporter.Add(new SupporterWeight(supporter, weight));
        }

        var contactPoints = supportersPerPoint.Count(count => count > 0);
        var totalWeight = bySupporter.Sum(entry => entry.Weight);
        return new CandidateContacts(contactPoints, totalWeight, bySupporter);
    }
}
