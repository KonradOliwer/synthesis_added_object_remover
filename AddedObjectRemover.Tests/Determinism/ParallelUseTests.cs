using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Determinism;

public class ParallelUseTests
{
    private static readonly string[] ParallelismApis = ["Parallel.For", "AsParallel"];

    /// <summary>The edge: the run's clock, its stopwatch for the total time, and the pipeline's own phase timers.</summary>
    private static readonly string[] ClockEdgeFiles = ["PhaseClock.cs", "Program.cs", "RemovalPipeline.cs"];

    [Fact]
    public void OnlyParallelMapRunsWorkInParallel()
    {
        var offenders = SourceFiles()
            .Where(file => Path.GetFileName(file) != "ParallelMap.cs")
            .Where(file => ParallelismApis.Any(api => File.ReadAllText(file).Contains(api, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheEdgeReadsTheClock()
    {
        var offenders = SourceFiles()
            .Where(file => !ClockEdgeFiles.Contains(Path.GetFileName(file)))
            .Where(file => File.ReadAllText(file).Contains("Stopwatch", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var projectFolder = Path.Combine(TestsFolder(), "..", "AddedObjectRemover");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(projectFolder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Select(Path.GetFullPath);
    }

    private static string TestsFolder([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
