namespace AddedObjectRemover.Tests.Entry;

public class AssetsProblemsTests
{
    private static readonly ArchiveProblem DataFolder = new(ArchiveProblemKind.DataFolderUnlistable, "Data", "Warning: could not list archives.");

    private static AssetProblem MeshProblem(string mesh) => new(mesh, AssetProblemKind.NotFound, $"[{mesh}] not found");

    private static UnexpectedError Error(string part) => new(part, "InvalidOperationException: boom", "Something is missing.");

    [Fact]
    public void AnArchiveProblemMetByManyReadsAppearsOnceInTheNextCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);

        Assert.Empty(capture.TakeArchive());
        log.Add(DataFolder);
        log.Add(DataFolder);
        Assert.Equal(DataFolder, Assert.Single(capture.TakeArchive()));
        Assert.Empty(capture.TakeArchive());
    }

    [Fact]
    public void AnArchiveProblemRecordedAfterACaptureIsHandedOutByTheNextOne()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);
        var late = new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "Warning: a.bsa unreadable.");
        log.Add(DataFolder);

        var first = capture.TakeArchive();
        log.Add(late);

        Assert.Equal(DataFolder, Assert.Single(first));
        Assert.Equal(late, Assert.Single(capture.TakeArchive()));
    }

    [Fact]
    public void MeshProblemsAreHandedOutPerCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);

        log.Add(MeshProblem(@"meshes\a.nif"));
        Assert.Single(capture.Take().Assets);
        log.Add(MeshProblem(@"meshes\b.nif"));
        Assert.Equal(@"meshes\b.nif", Assert.Single(capture.Take().Assets).Mesh);
    }

    [Fact]
    public void TakingMeshProblemsLeavesTheArchiveProblemsForTheirOwnCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);
        log.Add(DataFolder);
        log.Add(MeshProblem(@"meshes\a.nif"));

        Assert.Single(capture.Take().Assets);
        Assert.Equal(DataFolder, Assert.Single(capture.TakeArchive()));
    }

    [Fact]
    public void AnUnexpectedErrorMetByManyPartsAppearsOnceInTheNextCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);

        Assert.Empty(capture.TakeUnexpected());
        log.Add(Error("mesh a"));
        log.Add(Error("mesh a"));
        Assert.Equal(Error("mesh a"), Assert.Single(capture.TakeUnexpected()));
        Assert.Empty(capture.TakeUnexpected());
    }

    [Fact]
    public void UnexpectedErrorsAreHandedOutByMessageWhateverTheRecordingOrder()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);
        log.Add(Error("mesh b"));
        log.Add(Error("mesh a"));

        Assert.Equal(["mesh a", "mesh b"], capture.TakeUnexpected().Select(error => error.Part));
    }

    [Fact]
    public void TakingUnexpectedErrorsLeavesTheOtherProblemsForTheirOwnCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);
        log.Add(Error("mesh a"));
        log.Add(DataFolder);
        log.Add(MeshProblem(@"meshes\a.nif"));

        Assert.Single(capture.TakeUnexpected());
        Assert.Single(capture.Take().Assets);
        Assert.Equal(DataFolder, Assert.Single(capture.TakeArchive()));
    }
}
