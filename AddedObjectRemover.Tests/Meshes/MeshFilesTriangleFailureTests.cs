using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>Each mesh whose triangles cannot be read is counted once by its failure, and apart when its triangles were read fine earlier in the run.</summary>
public class MeshFilesTriangleFailureTests
{
    private const string Crate = @"meshes\crate.nif";

    private static readonly Box CrateBox = new(new System.Numerics.Vector3(-1, -2, 0), new System.Numerics.Vector3(1, 2, 3));

    private static MeshFiles Open(string dataFolder, AssetProblemLog problems, IUnexpectedErrors errors) =>
        new(new MeshFileSource(dataFolder, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, errors, BaseObjectShapes.SolidShapes);

    private static string NewDataFolder(string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, nameof(MeshFilesTriangleFailureTests), name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void AMissingMeshCountsOnceAsNotFoundAndNotAsFailedAfterAGoodRead()
    {
        var files = Open(NewDataFolder(nameof(AMissingMeshCountsOnceAsNotFoundAndNotAsFailedAfterAGoodRead)), new AssetProblemLog(), new UnexpectedErrors());

        Assert.Null(files.ReadTriangles(@"meshes\missing.nif"));
        Assert.Null(files.ReadTriangles(@"Meshes\Missing.nif"));

        var counts = files.CountTriangleFailures();
        Assert.Equal([KeyValuePair.Create(new MeshFailureCategory(MeshReadError.NotFound, null), 1)], counts.ByKind);
        Assert.Equal(0, counts.AfterGoodRead);
    }

    [Fact]
    public void AMeshThatFailsAfterItsTrianglesWereReadFineIsCountedAndListedApart()
    {
        var folder = NewDataFolder(nameof(AMeshThatFailsAfterItsTrianglesWereReadFineIsCountedAndListedApart));
        TestShapes.WriteMesh(folder, Crate, TestMeshes.BoxTriangles(CrateBox));
        var problems = new AssetProblemLog();
        var files = Open(folder, problems, new UnexpectedErrors());
        Assert.NotNull(files.ReadTriangles(Crate));
        File.Delete(Path.Combine(folder, MeshFileSource.InLooseFileForm(Crate)));

        Assert.Null(files.ReadTriangles(Crate));

        var counts = files.CountTriangleFailures();
        Assert.Equal([KeyValuePair.Create(new MeshFailureCategory(MeshReadError.NotFound, null), 1)], counts.ByKind);
        Assert.Equal(1, counts.AfterGoodRead);
        var afterGoodRead = Assert.Single(problems.Since(default), problem => problem.Kind == AssetProblemKind.TrianglesUnreadableAfterGoodRead);
        Assert.Equal(MeshReadError.NotFound, afterGoodRead.ReadFailure!.Kind);
    }

    [Fact]
    public void ATrianglesReadThatThrowsIsCountedAsAnUnexpectedErrorAndRecordedAsAnError()
    {
        var errors = new UnexpectedErrors();
        var files = new MeshFiles(
            new MeshFileSource(null!, GameRelease.SkyrimSE, [], new AssetProblemLog()), new NifGeometryReader(), new AssetProblemLog(), errors, BaseObjectShapes.SolidShapes);

        Assert.Null(files.ReadTriangles(@"meshes\broken.nif"));

        Assert.Equal([KeyValuePair.Create(new MeshFailureCategory(MeshReadError.UnexpectedException, null), 1)], files.CountTriangleFailures().ByKind);
        Assert.Equal(MeshReadingParts.MeshTriangles, Assert.Single(errors.Since(default)).Part);
    }

    [Fact]
    public void MeshesReadFineCountNoFailure()
    {
        var folder = NewDataFolder(nameof(MeshesReadFineCountNoFailure));
        TestShapes.WriteMesh(folder, Crate, TestMeshes.BoxTriangles(CrateBox));
        var files = Open(folder, new AssetProblemLog(), new UnexpectedErrors());

        Assert.NotNull(files.ReadTriangles(Crate));

        Assert.Equal(TriangleFailureCounts.None.ByKind, files.CountTriangleFailures().ByKind);
        Assert.Equal(0, files.CountTriangleFailures().AfterGoodRead);
    }
}
