namespace AddedObjectRemover;

/// <summary>Where an NPC's body size came from, best first; <see cref="Point"/> is the point-detection fallback.</summary>
internal enum NpcSizeSource
{
    BodyMesh,
    ObjectBounds,
    HumanoidApproximation,
    Point,
}

/// <summary>
/// The body of an NPC base in mesh-local units, plus the factor (race height × NPC height) that
/// the placed reference's scale multiplies. A <see cref="NpcSizeSource.Point"/> body is a
/// zero-size box at the placement point, so it is indexed and tested exactly like a sized body,
/// but can only ever be found stuck by standing inside the target's own shape.
/// </summary>
/// <param name="MeshPaths">Normalized body meshes for <see cref="NpcSizeSource.BodyMesh"/>; empty otherwise.</param>
/// <param name="LocalBox">Encloses the whole body; the origin point only for <see cref="NpcSizeSource.Point"/>.</param>
/// <param name="BoxTree"><see cref="LocalBox"/> as a closed mesh, used when no body mesh triangles apply.</param>
/// <param name="PointReason">Why the real size could not be determined; set only for <see cref="NpcSizeSource.Point"/>.</param>
internal sealed record NpcBody(
    NpcSizeSource Source,
    IReadOnlyList<string> MeshPaths,
    Box LocalBox,
    float HeightScale,
    MeshTriangleTree BoxTree,
    string? PointReason)
{
    /// <summary>Volume of the body's box at its height scale; zero for <see cref="NpcSizeSource.Point"/>.</summary>
    public float Volume
    {
        get
        {
            var size = LocalBox.Scaled(HeightScale).Size;
            return size.X * size.Y * size.Z;
        }
    }

    public static NpcBody FromMeshes(IReadOnlyList<string> meshPaths, Box localBox, float heightScale) =>
        new(NpcSizeSource.BodyMesh, meshPaths, localBox, heightScale, BoxMesh.CreateTree(localBox), null);

    public static NpcBody FromBox(NpcSizeSource source, Box localBox, float heightScale) =>
        new(source, [], localBox, heightScale, BoxMesh.CreateTree(localBox), null);

    public static NpcBody Point(string reason) =>
        new(NpcSizeSource.Point, [], Box.Zero, 1f, BoxMesh.CreateTree(Box.Zero), reason);
}
