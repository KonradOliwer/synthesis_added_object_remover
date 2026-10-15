using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Steps.BuildLogAndReports;
using Mutagen.Bethesda;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>A mesh path two records spell differently prints in one spelling, whichever was asked for first.</summary>
public class MeshSpellingTests
{
    private const string Upper = @"meshes\Rock.nif";
    private const string Lower = @"meshes\rock.nif";

    private static readonly string DataFolder = Path.Combine(AppContext.BaseDirectory, nameof(MeshSpellingTests));

    private static readonly string LockedFileFolder = Path.Combine(AppContext.BaseDirectory, nameof(MeshSpellingTests) + "Locked");

    private static (MeshFiles Files, AssetProblemLog Problems) Open(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        var problems = new AssetProblemLog();
        var files = new MeshFiles(
            new MeshFileSource(dataFolder, GameRelease.SkyrimSE, [], problems), new NifGeometryReader(), problems, new UnexpectedErrors(), BaseObjectShapes.SolidShapes);
        return (files, problems);
    }

    private static (MeshFiles Files, AssetProblemLog Problems) Open() => Open(DataFolder);

    [Theory]
    [InlineData(Upper, Lower)]
    [InlineData(Lower, Upper)]
    public void AMissingMeshPrintsTheSpellingThatSortsFirstWhicheverWasAskedFirst(string first, string second)
    {
        var (files, problems) = Open();

        files.Bounds.Of(first);
        files.Bounds.Of(second);

        Assert.Equal(Upper, Assert.Single(problems.Since(default)).Mesh);
        Assert.Equal(Upper, problems.PrintedSpelling(Lower));
    }

    [Theory]
    [InlineData(Upper, Lower)]
    [InlineData(Lower, Upper)]
    public void ATrianglesReadAlsoNotesItsSpelling(string first, string second)
    {
        var (files, problems) = Open();

        files.ReadTriangles(first);
        files.ReadTriangles(second);

        Assert.Equal(Upper, Assert.Single(problems.Since(default)).Mesh);
    }

    /// <summary>The loose path and the operating system's message print in the printed spelling, not the reading thread's.</summary>
    [Theory]
    [InlineData(Upper, Lower)]
    [InlineData(Lower, Upper)]
    public void AnUnreadableLooseMeshPrintsOneLineWhicheverSpellingWasReadFirst(string first, string second)
    {
        var looseFile = Path.Combine(LockedFileFolder, MeshFileSource.InLooseFileForm(Lower));
        Directory.CreateDirectory(Path.GetDirectoryName(looseFile)!);
        File.WriteAllBytes(looseFile, [0]);
        var (files, problems) = Open(LockedFileFolder);

        using (new FileStream(looseFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            files.Bounds.Of(first);
            files.Bounds.Of(second);
        }

        var unreadable = Assert.Single(problems.Since(default), problem => problem.Kind == AssetProblemKind.LooseFileUnreadable);
        var expectedPath = Path.Combine(LockedFileFolder, MeshFileSource.InLooseFileForm(Upper));
        Assert.Equal($"  [mesh] could not read loose file {expectedPath}: {unreadable.Detail}", MeshProblemText.Line(unreadable));
        Assert.Contains(expectedPath, unreadable.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMeshNobodyAskedForHasNoPrintedSpelling()
    {
        Assert.Throws<System.Diagnostics.UnreachableException>(() => new AssetProblemLog().PrintedSpelling(Upper));
    }
}
