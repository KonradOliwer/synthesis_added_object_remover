namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Compares what the code of each future project uses with the references the project table allows, before the
/// projects exist, so every missing edge shows at once. The test can go when step 80 checks the real references.
/// </summary>
public class ReferenceDryRunTests
{
    private const int ExamplesPerEdge = 3;

    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>
    /// Edges the code needs and the table lacks, which need a design decision before they are added or the code is changed.
    /// Each key is "From -> To"; the test fails when an entry is no longer missing.
    /// </summary>
    private static readonly Dictionary<string, string> EdgesWaitingForADecision = new()
    {
        ["AddedObjectRemover -> BuildLogAndReports.Contracts"] =
            "The entry's ConsoleLogSink prints a LogSection and its CsvReportOutput returns a ReportFilesResult, both Report contracts; the table lets the entry reference step contracts 23 and 26-44 but not 46.",
        ["FindTargetObjectsToKeep.Contracts -> PluginRecordFacts"] =
            "KeepReason holds the LinkFact that keeps an object; the table lets row 32 reference only 1, 23 and 28.",
        ["MeshFileReading -> OrderIndependentParallelWork"] =
            "MeshFileSource and AssetProblemLog count with AtomicCounter (the code may not use Interlocked outside the parallel and lookups tools); row 5 lacks row 9.",
        ["PluginRecordReadingAndWriting -> KeyedGroupingInInputOrder"] =
            "PlacedRecordReader groups navmeshes per space with KeyedGroups.GetOrAddList, whose only production user it is; row 3 lacks row 10.",
        ["RunSettingsValidation -> OrderIndependentParallelWork"] =
            "The validation builds the run's Execution (the worker count); row 21 lacks row 9, although row 52, which holds Execution in RunSettings, has it.",
    };

    [Fact]
    public void TheCodeUsesExactlyTheEdgesOfTheProjectTable()
    {
        var scan = FutureProjectReferences.Scan(Rules);

        var problems = scan.UnplacedFiles.Select(file => $"No row for the file {file}")
            .Concat(scan.AmbiguousTypes.Select(type => $"Type declared in several rows, so its uses cannot be attributed: {type}"))
            .Concat(FindMissingEdges(scan))
            .Concat(FindUnusedEdges(scan))
            .ToList();

        Assert.True(problems.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void ANamespaceBelongsToTheRowWhoseFolderIsItsLongestPrefix()
    {
        var rules = new ProjectRules([], [Row("Runner", "Run/RunAllSteps"), Row("Runner.Contracts", "Run/RunAllSteps.Contracts"), Row("Tool", "Tools/Tool")]);

        Assert.Equal("Runner", FutureProjectReferences.RowOfNamespace("AddedObjectRemover.Run.RunAllSteps.RunCaches", rules));
        Assert.Equal("Runner.Contracts", FutureProjectReferences.RowOfNamespace("AddedObjectRemover.Run.RunAllSteps.Contracts", rules));
        Assert.Null(FutureProjectReferences.RowOfNamespace("AddedObjectRemover.Run.RunAllStepsX", rules));
        Assert.Null(FutureProjectReferences.RowOfNamespace("System.Text", rules));
    }

    [Fact]
    public void TheScanSeesTheRunnerUsingTheSteps()
    {
        var scan = FutureProjectReferences.Scan(Rules);

        Assert.Contains(scan.References, reference => reference.From == "RunAllSteps" && reference.To == "RemoveTooCloseObjects");
        Assert.Contains(scan.References, reference => reference.From == "BuildLogAndReports" && reference.To == "BuildLogAndReports.Contracts");
    }

    private static IEnumerable<string> FindMissingEdges(ReferenceScan scan)
    {
        var missing = scan.References
            .Where(reference => !Rules.Find(reference.From).References.Contains(reference.To))
            .GroupBy(reference => $"{reference.From} -> {reference.To}")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var edge in missing.Where(group => !EdgesWaitingForADecision.ContainsKey(group.Key)))
            yield return $"Missing edge {edge.Key} ({edge.Select(reference => reference.File).Distinct().Count()} files), e.g. " +
                string.Join("; ", edge.Take(ExamplesPerEdge).Select(reference => $"{reference.File}: {reference.Use}"));
        foreach (var stale in EdgesWaitingForADecision.Keys.Except(missing.Select(group => group.Key)))
            yield return $"The list of edges waiting for a decision has {stale}, which is no longer missing";
    }

    /// <summary>
    /// Table edges nothing uses. Logic rows reference every tool by the "+ tools" rule, so only their other edges
    /// count; the runner and the entry reference contracts as a whole group, so they are not checked.
    /// </summary>
    private static IEnumerable<string> FindUnusedEdges(ReferenceScan scan)
    {
        var used = scan.References.Select(reference => (reference.From, reference.To)).ToHashSet();
        return Rules.Projects
            .Where(row => scan.RowsWithCode.Contains(row.Name) && row.Kind is not ("R" or "E" or "X"))
            .SelectMany(row => row.References
                .Where(reference => scan.RowsWithCode.Contains(reference) && !used.Contains((row.Name, reference)))
                .Where(reference => !(Rules.IsLogic(row) && Rules.Find(reference).Kind is "K" or "T"))
                .Select(reference => $"Unused edge {row.Name} -> {reference}"));
    }

    private static ProjectRule Row(string name, string folder) => new(name, "T", folder, [], []);
}
