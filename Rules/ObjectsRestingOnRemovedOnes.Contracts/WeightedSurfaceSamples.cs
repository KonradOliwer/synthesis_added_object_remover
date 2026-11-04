using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>Surface samples of a mesh, each weighted by its closeness to the mesh origin (<see cref="SampleWeights.ByOriginDistance"/>).</summary>
/// <param name="Bounds">The bounds of the mesh's triangles.</param>
/// <param name="Falloff">The L the weights were computed with.</param>
public sealed record WeightedSurfaceSamples(ImmutableArray<Vector3> Points, ImmutableArray<float> Weights, Box Bounds, float Falloff)
{
    public static WeightedSurfaceSamples Empty { get; } = new([], [], Box.Zero, Falloff: 0f);
}
