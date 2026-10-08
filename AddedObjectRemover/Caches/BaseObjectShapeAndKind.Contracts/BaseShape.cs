namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

/// <param name="MeshPath">Normalized path of the mesh the bounds came from; null for OBND or none.</param>
/// <param name="InvisibleKind">What kind of invisible object the base makes; null when it may be visible.</param>
/// <param name="Resolved">False when the base is not in the load order.</param>
/// <param name="EffectOnlyMesh">The base's mesh holds only effect-shader shapes, nothing solid.</param>
/// <param name="InvisibleForLackOfGeometry">
/// <paramref name="InvisibleKind"/> is only a marker kind guessed from the base having no geometry,
/// not from its record type or marker flag; a primitive reference of such a base is a trigger volume.
/// </param>
public sealed record BaseShape(
    Box Box,
    string? MeshPath,
    InvisibleObjectKind? InvisibleKind,
    bool Resolved = true,
    bool EffectOnlyMesh = false,
    bool InvisibleForLackOfGeometry = false)
{
    /// <summary>A zero-size box at the origin: what a missing or unknown base measures as.</summary>
    public static readonly BaseShape Missing = new(Box.Zero, null, null, Resolved: false);
}
