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
/// <param name="FailureKind">Short category of why the mesh failed to read; null when it did not fail.</param>
public sealed record MeshBounds(Box? Box, MeshReadStatus Status, MeshSource Source, bool HasFooterRoot, string? FailureKind);
