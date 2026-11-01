using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Weights of surface samples (<see cref="SurfaceSampler"/>): by closeness to the mesh origin, and split among overlapping sets of
/// hit samples. Results depend only on the inputs.
/// </summary>
public static class SampleWeights
{
    /// <summary>The falloff L of <see cref="ByOriginDistance"/>: the given fraction of the diagonal of the bounds.</summary>
    public static float FalloffOf(Box bounds, float fractionOfDiagonal) => bounds.Size.Length() * fractionOfDiagonal;

    /// <summary>Per point: weight = 1 / (1 + (d / L)^2), with d the point's distance from the origin and L the falloff.</summary>
    public static float[] ByOriginDistance(ImmutableArray<Vector3> points, float falloff) => points.Select(point => WeightAt(point.Length(), falloff)).ToArray();

    /// <returns>Per sample: how many of the hit sets include it.</returns>
    public static int[] CountHitsPerSample(IEnumerable<bool[]> hitSets, int sampleCount)
    {
        var hitsPerSample = new int[sampleCount];
        foreach (var hits in hitSets)
        {
            for (var i = 0; i < sampleCount; i++)
            {
                if (hits[i]) hitsPerSample[i]++;
            }
        }
        return hitsPerSample;
    }

    /// <summary>The weight one set of hits holds when each sample's weight is split equally among the sets that include it.</summary>
    /// <param name="hitsPerSample">Per sample: how many sets include it (<see cref="CountHitsPerSample"/>).</param>
    public static float WeightHeldBy(bool[] hits, float[] weights, int[] hitsPerSample)
    {
        var weight = 0f;
        for (var i = 0; i < weights.Length; i++)
        {
            if (hits[i]) weight += weights[i] / hitsPerSample[i];
        }
        return weight;
    }

    /// <remarks>A mesh without size has every point at its origin, so every point gets full weight.</remarks>
    private static float WeightAt(float distance, float falloff)
    {
        if (!(falloff > 0)) return 1f;
        var ratio = distance / falloff;
        return 1f / (1f + ratio * ratio);
    }
}
