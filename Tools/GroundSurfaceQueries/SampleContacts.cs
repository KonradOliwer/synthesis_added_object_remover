using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Which surface samples of a placed mesh are in contact with another placed mesh or with the terrain. The caller decides what
/// counts as contact, so the touch distance is passed in. Thread-safe; results depend only on the inputs.
/// </summary>
public static class SampleContacts
{
    /// <param name="samples">In the frame of <paramref name="samplesTransform"/>.</param>
    /// <param name="scratch">Reused buffer for triangle queries.</param>
    /// <returns>
    /// Per sample: whether it comes within the touch distance of the mesh's surface or is enclosed by it
    /// (<see cref="MeshContact.SamplesTouching"/>); none for a mesh without usable triangles.
    /// </returns>
    public static ImmutableArray<bool> TouchingMesh(
        ITriangleMeshes meshes,
        string meshPath,
        PlacedTransform meshTransform,
        ImmutableArray<Vector3> samples,
        PlacedTransform samplesTransform,
        float touchDistance,
        List<int> scratch)
    {
        using var lease = meshes.Acquire(meshPath);
        var touching = lease.Value is { } tree
            ? MeshContact.SamplesTouching(samples.AsSpan(), samplesTransform, tree, meshTransform, touchDistance, scratch)
            : new bool[samples.Length];
        return [.. touching];
    }

    /// <param name="space">A worldspace with terrain (<see cref="ITerrainHeights.HasTerrain"/>).</param>
    /// <param name="samples">In the frame of <paramref name="samplesTransform"/>.</param>
    /// <returns>Per sample: whether it lies at or below the terrain height plus the touch distance.</returns>
    public static ImmutableArray<bool> OnTerrain(ITerrainHeights terrain, RecordKey space, ImmutableArray<Vector3> samples, PlacedTransform samplesTransform, float touchDistance)
    {
        var hits = ImmutableArray.CreateBuilder<bool>(samples.Length);
        foreach (var sample in samples)
        {
            var world = samplesTransform.ToWorld(sample);
            hits.Add(terrain.TryGetHeight(space, new Vector2(world.X, world.Y), out var height) && world.Z <= height + touchDistance);
        }
        return hits.MoveToImmutable();
    }
}
