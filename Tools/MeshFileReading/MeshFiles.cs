using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>
/// The mesh reader of one run: finds a mesh file, parses it and remembers each mesh's bounds. Every
/// mesh is parsed for its bounds once, even under concurrent requests; triangles are read on demand
/// and not kept.
/// </summary>
internal sealed class MeshFiles(
    MeshFileSource source, NifGeometryReader reader, AssetProblemLog problems, IUnexpectedErrors unexpectedErrors, ShapeInclusion inclusion)
    : IMeshFiles, IMeshBounds
{
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

    public MeshBounds Of(string meshPath)
    {
        problems.NoteSpelling(meshPath);
        return _byMesh.Get(meshPath, () => ReadBounds(meshPath));
    }

    public IReadOnlyList<MeshBounds> Computed() => _byMesh.Contents();

    public MeshTriangles? ReadTriangles(string meshPath)
    {
        problems.NoteSpelling(meshPath);
        try
        {
            return ReadTrianglesOfMesh(meshPath);
        }
        catch (Exception ex) when (Failures.IsRecoverable(ex))
        {
            unexpectedErrors.Add(new PartFailure(MeshReadingParts.MeshTriangles, Failures.DescribeFailure(ex), MeshSubject(meshPath)));
            return null;
        }
    }

    private MeshTriangles? ReadTrianglesOfMesh(string meshPath)
    {
        var (result, _) = LoadAndParse(meshPath, includeTriangles: true);
        if (result.Failure is { } failure)
        {
            problems.Add(AssetProblem.TrianglesUnreadable(meshPath, failure));
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
            unexpectedErrors.Add(new PartFailure(MeshReadingParts.MeshShapes, Failures.DescribeFailure(ex), MeshSubject(meshPath)));
            return new MeshBounds(null, MeshReadStatus.Failed, MeshSource.NotFound, HasFooterRoot: false, MeshReadError.UnexpectedException, FailureExceptionType: null);
        }
    }

    private MeshBounds ReadBoundsOfMesh(string meshPath)
    {
        var (result, meshSource) = LoadAndParse(meshPath, includeTriangles: false);
        var hasFooterRoot = RecordFooterRoot(meshPath, result.FooterRoot);

        if (result is { Status: MeshReadStatus.Success, Geometry: { } geometry })
        {
            return new MeshBounds(Box.FromCorners(geometry.Min, geometry.Max), result.Status, meshSource, hasFooterRoot, FailureKind: null, FailureExceptionType: null);
        }
        var failure = result.Failure ?? throw new UnreachableException("A read that did not succeed carries its failure.");
        if (failure.Kind == MeshReadError.EffectOnly)
        {
            return new MeshBounds(null, result.Status, meshSource, hasFooterRoot, FailureKind: null, FailureExceptionType: null);
        }

        problems.Add(failure.Kind == MeshReadError.NotFound
            ? AssetProblem.NotFound(meshPath)
            : AssetProblem.Unreadable(meshPath, failure));
        return new MeshBounds(null, result.Status, meshSource, hasFooterRoot, failure.Kind, failure.ExceptionType);
    }

    private (NifReadResult Result, MeshSource Source) LoadAndParse(string meshPath, bool includeTriangles)
    {
        var (bytes, meshSource) = source.Load(meshPath);
        var result = bytes == null
            ? NifReadResult.Failed(MeshReadFailure.NotFound())
            : reader.ReadGeometry(bytes, includeTriangles, inclusion);
        if (result.StripFieldsMissing) problems.Add(NifShapes.StripFieldsMissing);
        if (result.MismatchedStrips > 0)
        {
            problems.Add(AssetProblem.ReadWarning(meshPath, result.MismatchedStrips));
        }
        return (result, meshSource);
    }

    private void ReportLoaderWarmUpFailure()
    {
        if (reader.LoaderWarmUp is not { Succeeded: false } startUp) return;
        unexpectedErrors.Add(new PartFailure(MeshReadingParts.LoaderStartUp, startUp.Failure, Subject: null));
    }

    /// <remarks>Meshes have no record key order; they are listed by path.</remarks>
    private static FailureSubject MeshSubject(string meshPath) => new(meshPath, Name: null, Position: 0);

    private bool RecordFooterRoot(string meshPath, NifRoot? footerRoot)
    {
        if (footerRoot is not { } root) return false;
        problems.Add(AssetProblem.FooterRootUsed(meshPath, new FooterRootBlocks(root.Index, root.LibraryRootIndex)));
        return true;
    }
}
