using System.Collections.Immutable;

namespace AddedObjectRemover;

public enum MeshReadStatus
{
    Success,

    /// <summary>The mesh could not be found, parsed, or holds invalid data (e.g. out-of-range coordinates).</summary>
    Failed,

    /// <summary>The mesh parsed fine but has no visible render geometry (e.g. only editor-marker shapes).</summary>
    NoRenderGeometry,

    /// <summary>The mesh's only render geometry uses effect shaders (fog, light rays, water spray, mist planes).</summary>
    EffectOnly,
}

/// <summary>Where a mesh file was found.</summary>
public enum MeshSource { NotFound, LooseFile, Archive }

/// <summary>What reading one mesh for its bounds gave.</summary>
/// <param name="Box">The render geometry's bounds in mesh-local space; null when the mesh is unreadable or has no render geometry.</param>
/// <param name="HasFooterRoot">The mesh's root node had to be taken from the file footer.</param>
/// <param name="FailureKind">Why the mesh failed to read; null when it did not fail (an effect-only mesh does not fail).</param>
/// <param name="FailureExceptionType">The library exception's type name; only for <see cref="MeshReadError.Malformed"/>.</param>
/// <param name="CollisionLayers">
/// The distinct layers of the mesh's collision rigid bodies, in ascending order; null when the mesh was not found, did not load or has no root node.
/// </param>
public sealed record MeshBounds(
    Box? Box,
    MeshReadStatus Status,
    MeshSource Source,
    bool HasFooterRoot,
    MeshReadError? FailureKind,
    string? FailureExceptionType,
    ImmutableArray<CollisionLayer>? CollisionLayers)
{
    /// <summary>The failure as the run statistics count it; null when the mesh did not fail.</summary>
    public MeshFailureCategory? FailureCategory => FailureKind is { } kind ? new MeshFailureCategory(kind, FailureExceptionType) : null;
}
