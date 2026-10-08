namespace AddedObjectRemover;

/// <summary>Opens the mesh reader of one run, counting the mesh shapes the caller's inclusion says.</summary>
public interface IMeshFilesFactory
{
    IMeshFiles Open(ShapeInclusion inclusion);
}

/// <summary>Finds mesh files the way the game does (loose file first, then archives) and reads them. Thread-safe.</summary>
public interface IMeshFiles
{
    IMeshBounds Bounds { get; }

    IMeshProblems Problems { get; }

    /// <summary>How many archives the index holds; the index is read by <see cref="PrepareReading"/> or on first use.</summary>
    int ArchivesIndexed { get; }

    /// <summary>Reads the archive index and warms up the mesh loader on the calling thread, so their warnings come in order and workers never wait on them.</summary>
    void PrepareReading();

    /// <summary>The "meshes\..." form with backslashes of a model path as the records give it.</summary>
    string NormalizeMeshPath(string givenPath);

    /// <summary>The mesh's triangles in mesh-local space; null (with a problem recorded) when the mesh cannot be read or has no triangles.</summary>
    MeshTriangles? ReadTriangles(string meshPath);
}

/// <summary>The render-geometry bounds of meshes, each read once with failures included. Thread-safe.</summary>
public interface IMeshBounds
{
    /// <param name="meshPath">A normalized mesh path; paths differing in case are the same mesh.</param>
    MeshBounds Of(string meshPath);

    /// <summary>The bounds of every mesh asked for so far, in no particular order.</summary>
    IReadOnlyList<MeshBounds> Computed();
}
