using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Report hands out each archive problem once, from its own mark on the run's problem log.</summary>
public class ReportProblemTakesTests
{
    private static readonly ArchiveProblem DataFolder = new(ArchiveProblemKind.DataFolderUnlistable, "Data", "denied");
    private const string DataFolderLine = "  Warning: could not list archives in Data: denied";

    [Fact]
    public void AnArchiveProblemMetByManyReadsAppearsOnceInTheNextTake()
    {
        var run = new FakeRun();

        Assert.Empty(run.Report.TakeArchiveProblemLines());
        run.Problems.Add(DataFolder);
        run.Problems.Add(DataFolder);
        Assert.Equal(DataFolderLine, Assert.Single(run.Report.TakeArchiveProblemLines()));
        Assert.Empty(run.Report.TakeArchiveProblemLines());
    }

    [Fact]
    public void AnArchiveProblemRecordedAfterATakeIsHandedOutByTheNextOne()
    {
        var run = new FakeRun();
        var late = new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "broken");
        run.Problems.Add(DataFolder);

        var first = run.Report.TakeArchiveProblemLines();
        run.Problems.Add(late);

        Assert.Equal(DataFolderLine, Assert.Single(first));
        Assert.Equal("  Warning: could not read archive a.bsa: broken", Assert.Single(run.Report.TakeArchiveProblemLines()));
    }

    private sealed class FakeRun
    {
        public FakeRun() => Report = new TestReport(new MeshFilesWithProblems(Problems), new UnexpectedErrors());

        public AssetProblemLog Problems { get; } = new();

        public TestReport Report { get; }
    }

    private sealed class MeshFilesWithProblems(AssetProblemLog problems) : IMeshFiles
    {
        public IMeshBounds Bounds => throw NotRead();

        public IMeshProblems Problems => problems;

        public int ArchivesIndexed => throw NotRead();

        public void PrepareReading() => throw NotRead();

        public string NormalizeMeshPath(string givenPath) => throw NotRead();

        public MeshTriangles? ReadTriangles(string meshPath) => throw NotRead();

        public TriangleFailureCounts CountTriangleFailures() => throw NotRead();

        private static InvalidOperationException NotRead() => new("The problem takes read only the problem log.");
    }
}
