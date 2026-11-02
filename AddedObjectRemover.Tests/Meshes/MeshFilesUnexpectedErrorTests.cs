using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using Mutagen.Bethesda;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>
/// A mesh whose reading hits a bug ends the run; it is not reported as an unreadable mesh. A defect of the shipped NIF library
/// would end every run, so its warm-up is run here against the shipped version.
/// </summary>
public class MeshFilesUnexpectedErrorTests
{
    private const string Mesh = @"meshes\broken.nif";

    /// <summary>A Data folder of null makes every mesh lookup throw before it reaches the file system.</summary>
    private static MeshFiles OpenBroken(AssetProblemLog problems) =>
        new(new MeshFileSource(null!, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, BaseObjectShapes.SolidShapes);

    [Fact]
    public void ABoundsReadThatHitsABugEndsTheRunAndReportsNothing()
    {
        var problems = new AssetProblemLog();

        Assert.Throws<ArgumentNullException>(() => OpenBroken(problems).Of(Mesh));

        Assert.Empty(problems.Since(default));
    }

    [Fact]
    public void ATrianglesReadThatHitsABugEndsTheRunAndReportsNothing()
    {
        var problems = new AssetProblemLog();

        Assert.Throws<ArgumentNullException>(() => OpenBroken(problems).ReadTriangles(Mesh));

        Assert.Empty(problems.Since(default));
    }

    [Fact]
    public void NiflySharpLoadsTheMinimalNifItSavedSoTheWarmUpBuildsItsBlockTypeCache()
    {
        var reader = new NifGeometryReader();

        reader.WarmUpLoader();
    }
}
