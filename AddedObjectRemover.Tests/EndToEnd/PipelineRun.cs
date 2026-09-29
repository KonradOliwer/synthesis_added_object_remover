using System.Globalization;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A report file written by a run: its name and its lines, with the run's folder masked.</summary>
internal sealed record ReportFile(string Name, IReadOnlyList<string> Lines);

/// <summary>Everything a run produces, masked so that it depends only on the fixture and the settings.</summary>
internal sealed record RunOutput(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, IReadOnlyList<ReportFile> Reports)
{
    /// <summary>One section per output: the log, the patch overrides, then each report file by name.</summary>
    public IReadOnlyList<string> ToGoldenLines() =>
    [
        "## log", .. Log,
        "## patch", .. Patch,
        .. Reports.SelectMany(report => (IEnumerable<string>)["## report " + report.Name, .. report.Lines]),
    ];
}

/// <summary>Runs the whole patcher in-process on the fixture load order, in a temporary folder of its own.</summary>
internal static class PipelineRun
{
    private const string DataFolderName = "Data";
    private const string OutputFolderName = "Output";

    public static RunOutput Execute(Settings settings, int workers)
    {
        var root = Directory.CreateTempSubdirectory("aor-e2e-");
        try
        {
            return Execute(settings, workers, root.FullName);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static RunOutput Execute(Settings settings, int workers, string root)
    {
        var dataFolder = Directory.CreateDirectory(Path.Combine(root, DataFolderName)).FullName;
        var outputFolder = Directory.CreateDirectory(Path.Combine(root, OutputFolderName)).FullName;
        var world = FixtureWorld.Create(dataFolder);
        var state = FixturePatcherState.Create(world.LoadOrder, dataFolder, Path.Combine(outputFolder, FixturePatcherState.PatchModKey.FileName));

        var log = CaptureInvariantConsole(() => Program.RunPatch(state, () => settings, workers));
        return new RunOutput(
            OutputMasks.MaskLog(log, root),
            PatchDump.Describe(state.PatchMod),
            ReadReports(RunConfigFactory.ResolveReportFolder(state.OutputPath.Path, settings), root));
    }

    /// <remarks>
    /// Formatting uses the invariant culture so the output does not depend on the machine; the
    /// culture flows into the parallel workers with the execution context.
    /// </remarks>
    private static IReadOnlyList<string> CaptureInvariantConsole(Action run)
    {
        var originalOut = Console.Out;
        var originalCulture = CultureInfo.CurrentCulture;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            run();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Console.SetOut(originalOut);
        }
        return captured.ToString().Split(Environment.NewLine).SkipLast(1).ToList();
    }

    private static List<ReportFile> ReadReports(string reportFolder, string root) =>
        Directory.Exists(reportFolder)
            ? Directory.EnumerateFiles(reportFolder)
                .Order(StringComparer.Ordinal)
                .Select(path => new ReportFile(Path.GetFileName(path), OutputMasks.MaskReport(File.ReadAllLines(path), root)))
                .ToList()
            : [];
}
