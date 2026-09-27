namespace AddedObjectRemover;

/// <summary>Where an NPC's body size came from, best first.</summary>
internal enum NpcSizeSource
{
    BodyMesh,
    ObjectBounds,
    HumanoidApproximation,
    Unknown,
}

/// <summary>
/// The body of an NPC base in mesh-local units, plus the factor (race height × NPC height) that
/// the placed reference's scale multiplies.
/// </summary>
/// <param name="MeshPaths">Normalized body meshes for <see cref="NpcSizeSource.BodyMesh"/>; empty otherwise.</param>
/// <param name="LocalBox">Encloses the whole body.</param>
/// <param name="BoxTree"><see cref="LocalBox"/> as a closed mesh; null only when the size is unknown.</param>
/// <param name="UnknownReason">Why the size is unknown; null otherwise.</param>
internal sealed record NpcBody(
    NpcSizeSource Source,
    IReadOnlyList<string> MeshPaths,
    Box LocalBox,
    float HeightScale,
    MeshTriangleTree? BoxTree,
    string? UnknownReason)
{
    public bool IsSized => Source != NpcSizeSource.Unknown;

    /// <summary>Volume of the body's box at its height scale.</summary>
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

    public static NpcBody Unknown(string reason) =>
        new(NpcSizeSource.Unknown, [], Box.Zero, 1f, null, reason);
}
