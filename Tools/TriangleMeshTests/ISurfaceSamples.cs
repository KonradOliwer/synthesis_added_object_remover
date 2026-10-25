using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <summary>Points spread over a mesh's surface (<see cref="SurfaceSampler"/>) and the bounds of its triangles, in mesh-local space.</summary>
public sealed record SampledSurface(ImmutableArray<Vector3> Points, Box Bounds);

/// <summary>Cache C15: the sampled surface of meshes by path, sampled on first use and shared by all callers.</summary>
public interface ISurfaceSamples
{
    /// <returns>Null when the mesh has no usable triangles or is too large.</returns>
    SampledSurface? Of(string meshPath);
}
