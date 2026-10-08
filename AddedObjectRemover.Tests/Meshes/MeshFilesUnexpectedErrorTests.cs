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
    private static MeshFiles OpenBroken(AssetProblemLog problems) =>
        new(new MeshFileSource(null!, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, BaseObjectRules.SolidShapes);

    [Fact]
    public void ABoundsReadThatThrowsGivesFailedBoundsAndReportsTheMesh()
    {
        var problems = new AssetProblemLog();
        var bounds = OpenBroken(problems).Of(Mesh);

        Assert.Equal(MeshReadStatus.Failed, bounds.Status);
        Assert.Null(bounds.Box);
        var error = Assert.Single(problems.UnexpectedSince(default));
        Assert.Equal("reading mesh shapes", error.Part);
        Assert.Equal(new ErrorSubject(SubjectKind.Mesh, Mesh, Position: 0), error.Subject);
        Assert.StartsWith("ArgumentNullException:", error.Failure, StringComparison.Ordinal);
        Assert.Equal("Objects using the mesh are treated as having no mesh shape.", error.Consequence);
    }

    [Fact]
    public void ATrianglesReadThatThrowsGivesNoTrianglesAndReportsTheMesh()
    {
        var problems = new AssetProblemLog();

        Assert.Null(OpenBroken(problems).ReadTriangles(Mesh));

        var error = Assert.Single(problems.UnexpectedSince(default));
        Assert.Equal("Objects using the mesh are treated as having no mesh triangles.", error.Consequence);
    }

    [Fact]
    public void AReadableMeshRecordsNoUnexpectedError()
    {
        var problems = new AssetProblemLog();
        var crate = new Box(new System.Numerics.Vector3(-1, -2, 0), new System.Numerics.Vector3(1, 2, 3));
        TestShapes.WriteMesh(DataFolder, @"meshes\crate.nif", TestMeshes.BoxTriangles(crate));
        var files = new MeshFiles(
            new MeshFileSource(DataFolder, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, BaseObjectRules.SolidShapes);

        Assert.Equal(MeshReadStatus.Success, files.Of(@"meshes\crate.nif").Status);
        Assert.Empty(problems.UnexpectedSince(default));
    }
}
