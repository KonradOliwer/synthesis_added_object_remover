using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>A mesh whose reading throws unexpectedly is reported as unreadable with its scope; other meshes are still read.</summary>
public class MeshFilesUnexpectedErrorTests
{
    private const string Mesh = @"meshes\broken.nif";

    private static readonly string DataFolder = Path.Combine(AppContext.BaseDirectory, nameof(MeshFilesUnexpectedErrorTests));

    /// <summary>A Data folder of null makes every mesh lookup throw before it reaches the file system.</summary>
    private static MeshFiles OpenBroken(AssetProblemLog problems, IUnexpectedErrors errors) =>
        new(new MeshFileSource(null!, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, errors, BaseObjectShapes.SolidShapes);

    [Fact]
    public void ABoundsReadThatThrowsGivesFailedBoundsAndReportsTheMesh()
    {
        var errors = new UnexpectedErrors();
        var bounds = OpenBroken(new AssetProblemLog(), errors).Of(Mesh);

        Assert.Equal(MeshReadStatus.Failed, bounds.Status);
        Assert.Null(bounds.Box);
        var error = Assert.Single(errors.Since(default));
        Assert.Equal(MeshReadingParts.MeshShapes, error.Part);
        Assert.Equal(new FailureSubject(Mesh, Name: null, Position: 0), error.Subject);
        Assert.StartsWith("ArgumentNullException:", error.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrianglesReadThatThrowsGivesNoTrianglesAndReportsTheMesh()
    {
        var errors = new UnexpectedErrors();

        Assert.Null(OpenBroken(new AssetProblemLog(), errors).ReadTriangles(Mesh));

        var error = Assert.Single(errors.Since(default));
        Assert.Equal(MeshReadingParts.MeshTriangles, error.Part);
        Assert.Equal(new FailureSubject(Mesh, Name: null, Position: 0), error.Subject);
    }

    [Fact]
    public void AReadableMeshRecordsNoUnexpectedError()
    {
        var problems = new AssetProblemLog();
        var errors = new UnexpectedErrors();
        var crate = new Box(new System.Numerics.Vector3(-1, -2, 0), new System.Numerics.Vector3(1, 2, 3));
        TestShapes.WriteMesh(DataFolder, @"meshes\crate.nif", TestMeshes.BoxTriangles(crate));
        var files = new MeshFiles(
            new MeshFileSource(DataFolder, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, errors, BaseObjectShapes.SolidShapes);

        Assert.Equal(MeshReadStatus.Success, files.Of(@"meshes\crate.nif").Status);
        Assert.Empty(errors.Since(default));
    }
}
