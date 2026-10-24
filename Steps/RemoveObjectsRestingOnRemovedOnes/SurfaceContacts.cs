using System.Numerics;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using static AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.SupportContactFinder;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Contact points of a placed mesh with what may hold it up. The mesh's surface is sampled with weights
/// (<see cref="WeightedSurfaceSamples"/>); a sample is a contact point for each mesh supporter whose surface it comes within
/// the touch distance of or is enclosed by (<see cref="MeshContact.SamplesTouching"/>), and for terrain when it lies at or
/// below terrain height plus the touch distance. Thread-safe; results depend only on the inputs.
/// </summary>
internal sealed class SurfaceContacts(ITerrainHeights terrain, ITriangleMeshes cache, float touchDistance)
{
    public WeightedSurfaceSamples SampleSurface(string meshPath)
    {
        using var lease = cache.Acquire(meshPath);
        return lease.Value is { } tree ? WeightedSurfaceSamples.Create(tree) : WeightedSurfaceSamples.Empty;
    }

    /// <returns>One per mesh supporter, in the given order, then one for the terrain when the space has terrain.</returns>
    public List<SupporterHits> FindHits(
        Vector3[] points, RecordKey space, PlacedTransform transform, IEnumerable<MeshSupporter> supporters, List<int> scratch)
    {
        var hits = supporters.Select(supporter => new SupporterHits(supporter.Supporter, FindSamplesTouchingMesh(points, transform, supporter, scratch))).ToList();
        if (terrain.HasTerrain(space)) hits.Add(new SupporterHits(Supporter.Terrain, FindSamplesOnTerrain(points, space, transform)));
        return hits;
    }

    /// <returns>Per sample: whether it is in contact with the supporter's mesh.</returns>
    private bool[] FindSamplesTouchingMesh(Vector3[] points, PlacedTransform transform, MeshSupporter supporter, List<int> scratch)
    {
        using var lease = cache.Acquire(supporter.MeshPath);
        return lease.Value is { } tree
            ? MeshContact.SamplesTouching(points, transform, tree, supporter.Transform, touchDistance, scratch)
            : new bool[points.Length];
    }

    /// <returns>Per sample: whether it lies at or below terrain height plus the touch distance.</returns>
    private bool[] FindSamplesOnTerrain(Vector3[] points, RecordKey space, PlacedTransform transform)
    {
        var hits = new bool[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var world = transform.ToWorld(points[i]);
            hits[i] = terrain.TryGetHeight(space, new Vector2(world.X, world.Y), out var height) && world.Z <= height + touchDistance;
        }
        return hits;
    }
}
