using System.Numerics;

namespace AddedObjectRemover;

internal enum SupporterType { Target, PlacedObject, Terrain }

internal readonly record struct Supporter
{
    private const int NoIndex = -1;

    private Supporter(SupporterType type, int index)
    {
        Type = type;
        Index = index;
    }

    public SupporterType Type { get; }

    /// <summary>The target index of a target; the slot in its space's <see cref="SupporterIndex"/> of a placed object.</summary>
    public int Index { get; }

    public static Supporter Terrain { get; } = new(SupporterType.Terrain, NoIndex);

    public static Supporter Target(int targetIndex) => new(SupporterType.Target, targetIndex);

    public static Supporter Placed(int slot) => new(SupporterType.PlacedObject, slot);
}

internal readonly record struct SupporterWeight(Supporter Supporter, float Weight);

/// <param name="ContactPoints">Surface samples in contact with at least one supporter.</param>
/// <param name="TotalWeight">Sum of the weights of all contact points.</param>
/// <param name="Supporters">Weight held by each supporter with at least one contact point, in a fixed order.</param>
internal sealed record CandidateContacts(int ContactPoints, float TotalWeight, IReadOnlyList<SupporterWeight> Supporters);

/// <summary>
/// Contact points of a target object with everything that may hold it up
/// (<see cref="AnchoringSupporterFinder"/>, and terrain). The object's surface is sampled with
/// weights (<see cref="WeightedSurfaceSamples"/>); a sample is a contact point for each mesh
/// supporter whose surface it comes within the touch distance of or is enclosed by
/// (<see cref="PointContactTest"/>), and for terrain when it lies at or below terrain height plus
/// the touch distance. A contact point touching several supporters splits its weight equally
/// among them. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class AnchoringContactFinder(
    IReadOnlyList<TargetObject> targets,
    TargetMeshPaths meshPaths,
    AnchoringSupporterFinder supporterFinder,
    TerrainHeights terrain,
    TriangleTreeCache cache,
    float touchDistance)
{
    private readonly record struct SupporterHits(Supporter Supporter, bool[] Hits);

    /// <param name="candidate">Index of a target object with a mesh.</param>
    public CandidateContacts FindContacts(int candidate)
    {
        var target = targets[candidate];
        var samples = SampleSurface(candidate);
        var hits = supporterFinder.FindMeshSupporters(candidate)
            .Select(supporter => new SupporterHits(supporter.Supporter, FindSamplesTouchingMesh(samples.Points, target.Transform, supporter)))
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
        return lease.Tree is { } tree ? WeightedSurfaceSamples.Create(tree) : WeightedSurfaceSamples.Empty;
    }

    /// <returns>Per sample: whether it is in contact with the supporter's mesh.</returns>
    private bool[] FindSamplesTouchingMesh(Vector3[] points, PlacedTransform candidateTransform, MeshSupporter supporter)
    {
        var hits = new bool[points.Length];
        using var lease = cache.Acquire(supporter.MeshPath);
        if (lease.Tree is not { } tree) return hits;

        var toSupporter = RelativeTransform.Create(from: candidateTransform, to: supporter.Transform);
        var localTolerance = touchDistance / supporter.Transform.Scale;
        var region = tree.Bounds.Grown(localTolerance);
        var scratch = new List<int>();
        for (var i = 0; i < points.Length; i++)
        {
            var point = toSupporter.Apply(points[i]);
            hits[i] = region.Contains(point) && PointContactTest.IsInContact(tree, point, localTolerance, scratch);
        }
        return hits;
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

    private static CandidateContacts SplitWeightsAmongSupporters(IReadOnlyList<SupporterHits> hitsBySupporter, float[] weights)
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
