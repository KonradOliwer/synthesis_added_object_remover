using System.Diagnostics;
using System.Numerics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal enum SupporterType { Target, OtherObject, Terrain }

/// <param name="Index">Target index for a target, index into its space's <see cref="OtherObjectIndex"/> for another mod's object; 0 for terrain.</param>
internal readonly record struct Supporter(SupporterType Type, int Index);

internal readonly record struct SupporterWeight(Supporter Supporter, float Weight);

/// <param name="ContactPoints">Surface samples in contact with at least one supporter.</param>
/// <param name="TotalWeight">Sum of the weights of all contact points.</param>
/// <param name="Supporters">Weight held by each supporter with at least one contact point, in a fixed order.</param>
internal sealed record CandidateContacts(int ContactPoints, float TotalWeight, IReadOnlyList<SupporterWeight> Supporters);

/// <summary>
/// Contact points of a target object with everything that may hold it up: nearby target objects
/// (removed or not), visible objects of other mods, and terrain. The object's surface is sampled
/// (<see cref="SurfaceSampler"/>); a sample is a contact point for each supporter whose surface it
/// comes within the touch distance of or is embedded in (<see cref="PointContactTest"/>), and for
/// terrain when it lies at or below terrain height plus the touch distance.
///
/// Each sample is weighted by its closeness to the mesh origin in the object's local frame,
/// weight = 1 / (1 + (d / L)^2), with d the sample's distance from the origin and
/// L = <see cref="OriginFalloffFraction"/> x the local bounds diagonal: most placed objects have
/// their origin where they rest, so contact there anchors more than contact far away. A contact
/// point touching several supporters splits its weight equally among them.
/// Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class AnchoringContactFinder(
    IReadOnlyList<TargetObject> targets,
    string?[] meshPaths,
    TouchCandidateFinder targetFinder,
    IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
    BaseObjectShapeProvider shapes,
    TerrainHeights terrain,
    TriangleTreeCache cache,
    float touchDistance)
{
    /// <summary>Distance from the origin, as a fraction of the local bounds diagonal, at which a contact point has half weight.</summary>
    public const float OriginFalloffFraction = 0.25f;

    /// <summary>Surface samples of one mesh and their weights by closeness to its origin.</summary>
    private sealed record MeshSamples(Vector3[] Points, float[] Weights);

    /// <summary>A mesh supporter placed in the world.</summary>
    private readonly record struct PlacedSupporter(Supporter Supporter, PlacedTransform Transform, string MeshPath);

    private readonly LazyCache<string, MeshSamples> _samplesByMesh = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="candidate">Index of a target object with a mesh.</param>
    public CandidateContacts FindContacts(int candidate)
    {
        var samples = GetSamples(MeshPathOf(candidate));
        var supporters = CollectMeshSupporters(candidate);
        var hits = supporters.Select(supporter => MarkMeshContacts(samples.Points, targets[candidate].Transform, supporter)).ToList();
        var supporterIds = supporters.Select(supporter => supporter.Supporter).ToList();
        if (terrain.HasTerrain(targets[candidate].SpaceKey))
        {
            hits.Add(MarkTerrainContacts(samples.Points, targets[candidate]));
            supporterIds.Add(new Supporter(SupporterType.Terrain, 0));
        }
        return SumWeights(supporterIds, hits, samples.Weights);
    }

    private MeshSamples GetSamples(string meshPath) =>
        _samplesByMesh.GetOrCreate(meshPath, () => SampleMesh(meshPath));

    private MeshSamples SampleMesh(string meshPath)
    {
        using var lease = cache.Acquire(meshPath);
        if (lease.Tree is not { } tree) return new MeshSamples([], []);

        var points = SurfaceSampler.Sample(tree);
        var falloff = tree.Bounds.Size.Length() * OriginFalloffFraction;
        var weights = points.Select(point => WeightByOriginDistance(point.Length(), falloff)).ToArray();
        return new MeshSamples(points, weights);
    }

    /// <remarks>A mesh without size has every point at its origin, so every point gets full weight.</remarks>
    private static float WeightByOriginDistance(float distance, float falloff)
    {
        if (!(falloff > 0)) return 1f;
        var ratio = distance / falloff;
        return 1f / (1f + ratio * ratio);
    }

    private List<PlacedSupporter> CollectMeshSupporters(int candidate)
    {
        var supporters = targetFinder.FindNeighbors(candidate)
            .Select(neighbor => new PlacedSupporter(new Supporter(SupporterType.Target, neighbor), targets[neighbor].Transform, MeshPathOf(neighbor)))
            .ToList();
        supporters.AddRange(CollectOtherSupporters(targets[candidate]));
        return supporters;
    }

    /// <summary>Visible other-mod objects with a mesh whose oriented box comes within the touch distance of the candidate's.</summary>
    private IEnumerable<PlacedSupporter> CollectOtherSupporters(TargetObject candidate)
    {
        var index = indexes[candidate.SpaceKey];
        var candidateBox = OrientedBox.FromLocal(shapes.GetLocalBox(candidate.Base), candidate.Transform);
        var slots = new List<int>();
        index.Grid.Collect(candidateBox.WorldAabb(touchDistance + TooCloseSearch.OtherObjectSearchMargin), slots);
        foreach (var otherIndex in slots.Distinct().Order())
        {
            if (!index.TryGetVisibleCenter(otherIndex, out _)) continue;
            var other = index[otherIndex];
            if (shapes.GetMeshPath(other.Base) is not { } meshPath) continue;

            var transform = new PlacedTransform(other.Position, Geometry.RotationFromEuler(other.Rotation), other.Scale);
            var otherBox = OrientedBox.FromLocal(shapes.GetLocalBox(other.Base), transform);
            if (!candidateBox.Intersects(otherBox, touchDistance)) continue;

            yield return new PlacedSupporter(new Supporter(SupporterType.OtherObject, otherIndex), transform, meshPath);
        }
    }

    /// <returns>Per sample: whether it is in contact with the supporter's mesh.</returns>
    private bool[] MarkMeshContacts(Vector3[] points, PlacedTransform candidateTransform, PlacedSupporter supporter)
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
    private bool[] MarkTerrainContacts(Vector3[] points, TargetObject candidate)
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

    private static CandidateContacts SumWeights(IReadOnlyList<Supporter> supporters, IReadOnlyList<bool[]> hits, float[] weights)
    {
        var supportersPerPoint = new int[weights.Length];
        foreach (var supporterHits in hits)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                if (supporterHits[i]) supportersPerPoint[i]++;
            }
        }

        var bySupporter = new List<SupporterWeight>();
        for (var s = 0; s < supporters.Count; s++)
        {
            var weight = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                if (hits[s][i]) weight += weights[i] / supportersPerPoint[i];
            }
            if (weight > 0) bySupporter.Add(new SupporterWeight(supporters[s], weight));
        }

        var contactPoints = supportersPerPoint.Count(count => count > 0);
        var totalWeight = bySupporter.Sum(entry => entry.Weight);
        return new CandidateContacts(contactPoints, totalWeight, bySupporter);
    }

    /// <remarks>Candidates and target supporters are only taken from targets with a mesh.</remarks>
    private string MeshPathOf(int target) =>
        meshPaths[target] ?? throw new UnreachableException($"Target {target} has no mesh but was used for anchoring.");
}
