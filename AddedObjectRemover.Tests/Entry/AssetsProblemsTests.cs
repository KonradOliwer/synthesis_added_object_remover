namespace AddedObjectRemover.Tests.Entry;

public class AssetsProblemsTests
{
    private static readonly ArchiveProblem WarmUp = new(ArchiveProblemKind.NifLoaderWarmUpFailed, "NIF loader", "Warning: warm-up failed.");

    private static AssetProblem MeshProblem(string mesh) => new(mesh, AssetProblemKind.NotFound, $"[{mesh}] not found");

    [Fact]
    public void AnArchiveProblemMetByManyReadsAppearsOnceInTheNextCapture()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);

        Assert.Empty(capture.Take().Archive);
        log.Add(WarmUp);
        log.Add(WarmUp);
        Assert.Equal(WarmUp, Assert.Single(capture.Take().Archive));
        Assert.Empty(capture.Take().Archive);
    }

    [Fact]
    public void AProblemRecordedAfterACaptureIsHandedOutByTheNextOne()
    {
        var log = new AssetProblemLog();
        var capture = new ProblemCapture(log);
        var late = new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "Warning: a.bsa unreadable.");
        log.Add(WarmUp);

        var first = capture.Take();
        log.Add(late);

        Assert.Equal(WarmUp, Assert.Single(first.Archive));
        Assert.Equal(late, Assert.Single(capture.Take().Archive));
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
}
