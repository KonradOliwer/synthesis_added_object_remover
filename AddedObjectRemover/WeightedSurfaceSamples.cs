using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Surface samples of a mesh (<see cref="SurfaceSampler"/>), each weighted by its closeness to the
/// mesh origin in the mesh's local frame: weight = 1 / (1 + (d / L)^2), with d the sample's
/// distance from the origin and L = <see cref="OriginFalloffFraction"/> x the diagonal of the
/// triangles' bounds. Most placed objects have their origin where they rest, so contact there
/// anchors more than contact far away.
/// </summary>
internal sealed record WeightedSurfaceSamples(Vector3[] Points, float[] Weights)
{
    /// <summary>Distance from the origin, as a fraction of the bounds diagonal, at which a sample has half weight.</summary>
    private const float OriginFalloffFraction = 0.25f;

    public static WeightedSurfaceSamples Empty { get; } = new([], []);

    public static WeightedSurfaceSamples Create(MeshTriangleTree tree)
    {
        var points = SurfaceSampler.Sample(tree);
        var falloff = tree.Bounds.Size.Length() * OriginFalloffFraction;
        return new WeightedSurfaceSamples(points, points.Select(point => WeightByOriginDistance(point.Length(), falloff)).ToArray());
    }

    /// <remarks>A mesh without size has every point at its origin, so every point gets full weight.</remarks>
    private static float WeightByOriginDistance(float distance, float falloff)
    {
        if (!(falloff > 0)) return 1f;
        var ratio = distance / falloff;
        return 1f / (1f + ratio * ratio);
    }
}
