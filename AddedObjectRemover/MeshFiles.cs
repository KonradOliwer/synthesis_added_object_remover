namespace AddedObjectRemover;

/// <summary>
/// The mesh reader of one run: finds a mesh file, parses it and remembers each mesh's bounds. Every
/// mesh is parsed for its bounds once, even under concurrent requests; triangles are read on demand
/// and not kept.
/// </summary>
internal sealed class MeshFiles(MeshFileSource source, NifGeometryReader reader, AssetProblemLog problems, ShapeInclusion inclusion)
    : IMeshFiles, IMeshBounds
{
    private const string UnexpectedErrorKind = "unexpected error";

    private readonly ComputedOncePerKey<string, MeshBounds> _byMesh = new(Publication.BuiltOnce, StringComparer.OrdinalIgnoreCase);

    public IMeshBounds Bounds => this;

    public IMeshProblems Problems => problems;

    public int ArchivesIndexed => source.ArchivesIndexed;

    public void PrepareReading()
    {
        source.BuildArchiveIndexNow();
        reader.WarmUpLoader();
        ReportLoaderWarmUpFailure();
    }

    public string NormalizeMeshPath(string givenPath) => MeshFileSource.NormalizeMeshPath(givenPath);

    public MeshBounds Of(string meshPath) =>
        _byMesh.Get(meshPath, () => ReadBounds(meshPath));

    public IReadOnlyList<MeshBounds> Computed() => _byMesh.Contents();

    public MeshTriangles? ReadTriangles(string meshPath)
    {
        try
        {
            return ReadTrianglesOfMesh(meshPath);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            problems.Add(Failures.Unexpected(
                "reading mesh triangles", "Objects using the mesh are treated as having no mesh triangles.", ex, MeshSubject(meshPath)));
            return null;
        }
    }

    private MeshTriangles? ReadTrianglesOfMesh(string meshPath)
    {
        var (result, _) = LoadAndParse(meshPath, includeTriangles: true);
        if (result.Status != MeshReadStatus.Success)
        {
            problems.Add(new AssetProblem(
                meshPath, AssetProblemKind.TrianglesUnreadable, $"  [mesh] triangles unreadable: {meshPath} ({result.Error})"));
            return null;
        }
        return result.Geometry is { Triangles.TriangleCount: > 0 } geometry ? geometry.Triangles : null;
    }

    private MeshBounds ReadBounds(string meshPath)
    {
        try
        {
            return ReadBoundsOfMesh(meshPath);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            problems.Add(Failures.Unexpected(
                "reading mesh shapes", "Objects using the mesh are treated as having no mesh shape.", ex, MeshSubject(meshPath)));
            return new MeshBounds(null, MeshReadStatus.Failed, MeshSource.NotFound, HasFooterRoot: false, UnexpectedErrorKind);
        }
    }

    private MeshBounds ReadBoundsOfMesh(string meshPath)
    {
        var (result, meshSource) = LoadAndParse(meshPath, includeTriangles: false);
        var hasFooterRoot = RecordFooterRoot(meshPath, result.FooterRoot);

        if (result is { Status: MeshReadStatus.Success, Geometry: { } geometry })
        {
            return new MeshBounds(Box.FromCorners(geometry.Min, geometry.Max), result.Status, meshSource, hasFooterRoot, FailureKind: null);
        }
        if (result.Status == MeshReadStatus.EffectOnly)
        {
            return new MeshBounds(null, result.Status, meshSource, hasFooterRoot, FailureKind: null);
        }

        problems.Add(result.ErrorKind == NifReadResult.NotFoundKind
            ? new AssetProblem(meshPath, AssetProblemKind.NotFound, $"  [mesh] not found: {meshPath}")
            : new AssetProblem(meshPath, AssetProblemKind.Unreadable, $"  [mesh] unreadable: {meshPath} ({result.Error})"));
        return new MeshBounds(null, result.Status, meshSource, hasFooterRoot, result.ErrorKind ?? result.Status.ToString());
    }

    private (NifReadResult Result, MeshSource Source) LoadAndParse(string meshPath, bool includeTriangles)
    {
        var (bytes, meshSource) = source.Load(meshPath);
        var result = bytes == null
            ? NifReadResult.Failed(NifReadResult.NotFoundKind, "mesh file not found")
            : reader.ReadGeometry(bytes, includeTriangles, inclusion);
        if (result.StripFieldsMissing) problems.Add(NifShapes.StripFieldsMissing);
        if (result.Warning != null)
        {
            problems.Add(new AssetProblem(meshPath, AssetProblemKind.ReadWarning, $"  [mesh] {meshPath}: {result.Warning}"));
        }
        return (result, meshSource);
    }

    private void ReportLoaderWarmUpFailure()
    {
        if (reader.LoaderWarmUpFailure is not { } failure) return;
        problems.Add(new UnexpectedError("the NIF loader start-up", failure, "Meshes are read one at a time, which is slower; the results are not affected."));
    }

    /// <remarks>Meshes have no record key order; they are listed by path.</remarks>
    private static ErrorSubject MeshSubject(string meshPath) => new(SubjectKind.Mesh, meshPath, Position: 0);

    private bool RecordFooterRoot(string meshPath, NifRoot? footerRoot)
    {
        if (footerRoot is not { } root) return false;
        problems.Add(new AssetProblem(
            meshPath,
            AssetProblemKind.FooterRoot,
            $"  [mesh] {meshPath}: footer root block {root.Index} used instead of first-node root block {root.LibraryRootIndex}"));
        return true;
    }
}
