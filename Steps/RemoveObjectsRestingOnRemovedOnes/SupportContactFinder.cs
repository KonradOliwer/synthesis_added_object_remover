using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a target object with everything that may hold it up
/// (<see cref="SupporterFinder"/>, and terrain). The object's surface is sampled with
/// weights (<see cref="WeightedSurfaceSamples"/>); a sample is a contact point for each mesh
/// supporter whose surface it comes within the touch distance of or is enclosed by
/// (<see cref="MeshContact.SamplesTouching"/>), and for terrain when it lies at or below terrain height plus
/// the touch distance. A contact point touching several supporters splits its weight equally
/// among them. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SupportContactFinder(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    SupporterFinder supporterFinder,
    ITerrainHeights terrain,
    ITriangleMeshes cache,
    float touchDistance)
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
        var samples = SampleSurface(candidate);
        var hits = supporterFinder.FindMeshSupporters(candidate, scratch)
            .Select(supporter => new SupporterHits(supporter.Supporter, FindSamplesTouchingMesh(samples.Points, target.Transform, supporter, scratch)))
            .ToList();
        if (terrain.HasTerrain(target.SpaceKey))
        {
            hits.Add(new SupporterHits(Supporter.Terrain, FindSamplesOnTerrain(samples.Points, target)));
        }
        var contacts = SplitWeightsAmongSupporters(hits, samples.Weights);
        return new FoundContacts(contacts, ZeroWeightDiagnosis.Of(candidate, meshPaths.Get(candidate), samples, hits, contacts));
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
