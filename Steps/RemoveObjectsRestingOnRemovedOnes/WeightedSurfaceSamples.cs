using System.Numerics;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Surface samples of a mesh (<see cref="SurfaceSampler"/>), each weighted by its closeness to the
/// mesh origin in the mesh's local frame: weight = 1 / (1 + (d / L)^2), with d the sample's
/// distance from the origin and L = <see cref="OriginFalloffFraction"/> x the diagonal of the
/// triangles' bounds. Most placed objects have their origin where they rest, so contact there
/// anchors more than contact far away.
/// </summary>
/// <param name="Falloff">The L the weights were computed with.</param>
internal sealed record WeightedSurfaceSamples(Vector3[] Points, float[] Weights, Box Bounds, float Falloff)
{
    /// <summary>Distance from the origin, as a fraction of the bounds diagonal, at which a sample has half weight.</summary>
    private const float OriginFalloffFraction = 0.25f;

    private const int SamplesPerMesh = 4096;

    public static WeightedSurfaceSamples Empty { get; } = new([], [], Box.Zero, Falloff: 0f);

    /// <summary>L of the weight formula, recomputed from <see cref="Bounds"/>.</summary>
    public float MeshSize => MeshSizeOf(Bounds);

    private static float MeshSizeOf(Box bounds) => bounds.Size.Length() * OriginFalloffFraction;

    public static WeightedSurfaceSamples Create(MeshTriangleTree tree)
    {
        var points = SurfaceSampler.Sample(tree, SamplesPerMesh);
        var falloff = MeshSizeOf(tree.Bounds);
        return new WeightedSurfaceSamples(points, points.Select(point => WeightByOriginDistance(point.Length(), falloff)).ToArray(), tree.Bounds, falloff);
    }

    /// <remarks>A mesh without size has every point at its origin, so every point gets full weight.</remarks>
    private static float WeightByOriginDistance(float distance, float falloff)
    {
        if (!(falloff > 0)) return 1f;
        var ratio = distance / falloff;
        return 1f / (1f + ratio * ratio);
    }
}
