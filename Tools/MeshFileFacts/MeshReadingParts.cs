namespace AddedObjectRemover;

/// <summary>The parts of mesh reading the mesh reader records its unexpected errors under.</summary>
public static class MeshReadingParts
{
    private const string Owner = "MeshFileFacts";

    /// <summary>Reading one mesh's shapes for its bounds; the subject is the mesh.</summary>
    public static readonly PartKey MeshShapes = new(Owner, nameof(MeshShapes));

    /// <summary>Reading one mesh's triangles; the subject is the mesh.</summary>
    public static readonly PartKey MeshTriangles = new(Owner, nameof(MeshTriangles));

    /// <summary>Starting the mesh loader before the meshes are read; it has no subject.</summary>
    public static readonly PartKey LoaderStartUp = new(Owner, nameof(LoaderStartUp));
}
