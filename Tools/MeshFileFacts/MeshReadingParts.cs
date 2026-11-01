namespace AddedObjectRemover;

/// <summary>The parts of mesh reading the mesh reader records its unexpected errors under.</summary>
public static class MeshReadingParts
{
    private const string Owner = "MeshFileFacts";

    /// <summary>Starting the mesh loader before the meshes are read; it has no subject.</summary>
    public static readonly PartKey LoaderStartUp = new(Owner, nameof(LoaderStartUp));
}
