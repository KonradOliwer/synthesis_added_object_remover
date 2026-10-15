namespace AddedObjectRemover;

/// <summary>What a mesh shape or node is, as far as the mesh file itself tells.</summary>
public enum MeshShapeKind
{
    /// <summary>Real render geometry.</summary>
    Solid,

    /// <summary>Drawn with an effect shader (fog, light rays, water spray, mist planes): nothing solid.</summary>
    EffectShader,

    /// <summary>Named like a Creation Kit editor marker.</summary>
    EditorMarker,
}

/// <summary>Which shapes of a mesh the caller counts as its geometry.</summary>
/// <param name="CountedKinds">A shape of any other kind is skipped.</param>
/// <param name="SkippedAncestorKinds">Shapes under a node of such a kind are skipped.</param>
/// <param name="IncludeHidden">Whether shapes and nodes with the hidden flag count; without it, shapes under a hidden node are skipped too.</param>
/// <param name="RetryIncludingHiddenWhenEmpty">When nothing counted but something was hidden, the mesh is read again with hidden shapes included.</param>
/// <param name="EditorMarkerNamePart">A shape or node whose name contains this (ignoring case) is an editor marker.</param>
public sealed record ShapeInclusion(
    IReadOnlySet<MeshShapeKind> CountedKinds,
    IReadOnlySet<MeshShapeKind> SkippedAncestorKinds,
    bool IncludeHidden,
    bool RetryIncludingHiddenWhenEmpty,
    string EditorMarkerNamePart)
{
    public bool IsEditorMarkerName(string? name) =>
        name != null && name.Contains(EditorMarkerNamePart, StringComparison.OrdinalIgnoreCase);

    public ShapeInclusion WithHiddenIncluded() => this with { IncludeHidden = true };
}
