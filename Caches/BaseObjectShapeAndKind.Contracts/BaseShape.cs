namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

/// <param name="MeshPath">Normalized path of the mesh the bounds came from; null for OBND or none.</param>
/// <param name="InvisibleKind">What kind of invisible object the base makes; null when it may be visible.</param>
/// <param name="Resolved">False when the base is not in the load order.</param>
/// <param name="EffectOnlyMesh">The base's mesh holds only effect-shader shapes, nothing solid.</param>
/// <param name="InvisibleForLackOfGeometry">
/// <paramref name="InvisibleKind"/> is only a marker kind guessed from the base having no geometry,
/// not from its record type or marker flag; a primitive reference of such a base is a trigger volume.
/// </param>
/// <param name="CollisionBlocksMovement">
/// Whether the mesh has collision that stops characters walking through it; null when the bounds did not come from a mesh and the mesh
/// is not one without render geometry, whose collision is read as well.
/// </param>
/// <param name="PrimitiveWithoutLayerIsWalkThrough">
/// The base is an activator or an acoustic space. A primitive reference of such a base that stores no collision layer has the default
/// layer of its kind (trigger, acoustic space), which does not stop characters.
/// </param>
/// <param name="EffectMeshBox">For an effect-only mesh, the bounds of its effect shapes; null otherwise or when they hold no point.</param>
public sealed record BaseShape(
    Box Box,
    string? MeshPath,
    InvisibleObjectKind? InvisibleKind,
    bool Resolved = true,
    bool EffectOnlyMesh = false,
    bool InvisibleForLackOfGeometry = false,
    bool? CollisionBlocksMovement = null,
    bool PrimitiveWithoutLayerIsWalkThrough = false,
    Box? EffectMeshBox = null)
{
    /// <summary>A zero-size box at the origin: what a missing or unknown base measures as.</summary>
    public static readonly BaseShape Missing = new(Box.Zero, null, null, Resolved: false);

    /// <summary>The box of what the game draws: the effect shapes' bounds for an effect-only mesh that has them, <see cref="Box"/> otherwise.</summary>
    public Box ShownBox => EffectMeshBox ?? Box;
}
