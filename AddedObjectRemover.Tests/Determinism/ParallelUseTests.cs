using AddedObjectRemover.Tests.Architecture;

namespace AddedObjectRemover.Tests.Determinism;

public class ParallelUseTests
{
    private static readonly string[] ParallelismApis = ["Parallel.For", "AsParallel"];

    /// <summary>The edge: the run's clock, its stopwatch for the total time, and the log and report writing that time themselves.</summary>
    private static readonly string[] ClockEdgeFiles = ["PhaseClock.cs", "Program.cs", "RunLog.cs", "ReportFolder.cs"];

    [Fact]
    public void OnlyParallelMapRunsWorkInParallel()
    {
        var offenders = ProductionSources.Files()
            .Where(file => Path.GetFileName(file) != "ParallelMap.cs")
            .Where(file => ParallelismApis.Any(api => File.ReadAllText(file).Contains(api, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheEdgeReadsTheClock()
    {
        var offenders = ProductionSources.Files()
            .Where(file => !ClockEdgeFiles.Contains(Path.GetFileName(file)))
            .Where(file => File.ReadAllText(file).Contains("Stopwatch", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }
}
