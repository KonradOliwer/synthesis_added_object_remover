using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>A mesh whose reading hits a bug ends the run; it is not reported as an unreadable mesh.</summary>
public class MeshFilesUnexpectedErrorTests
{
    private const string Mesh = @"meshes\broken.nif";

    private static readonly string DataFolder = Path.Combine(AppContext.BaseDirectory, nameof(MeshFilesUnexpectedErrorTests));

    /// <summary>A Data folder of null makes every mesh lookup throw before it reaches the file system.</summary>
    private static MeshFiles OpenBroken(AssetProblemLog problems, IUnexpectedErrors errors) =>
        new(new MeshFileSource(null!, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, errors, BaseObjectShapes.SolidShapes);

    [Fact]
    public void ABoundsReadThatHitsABugEndsTheRunAndReportsNothing()
    {
        var errors = new UnexpectedErrors();

        Assert.Throws<ArgumentNullException>(() => OpenBroken(new AssetProblemLog(), errors).Of(Mesh));

        Assert.Empty(errors.Since(default));
    }

    [Fact]
    public void ATrianglesReadThatHitsABugEndsTheRunAndReportsNothing()
    {
        var errors = new UnexpectedErrors();

        Assert.Throws<ArgumentNullException>(() => OpenBroken(new AssetProblemLog(), errors).ReadTriangles(Mesh));

        Assert.Empty(errors.Since(default));
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
