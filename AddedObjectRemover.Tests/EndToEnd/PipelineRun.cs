using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A report file written by a run: its name and its lines, with the run's folder masked.</summary>
internal sealed record ReportFile(string Name, IReadOnlyList<string> Lines);

/// <summary>Everything a run produces, masked so that it depends only on the fixture and the settings.</summary>
/// <param name="UnmaskedLog">The log as printed, for checks the masks would hide; not part of the expected-output lines.</param>
internal sealed record RunOutput(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, IReadOnlyList<ReportFile> Reports, IReadOnlyList<string> UnmaskedLog)
{
    /// <summary>One section per output: the log, the patch overrides, then each report file by name.</summary>
    public IReadOnlyList<string> ToExpectedOutputLines() =>
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

        var log = CaptureConsole(() => Program.RunPatch(state, () => settings, workers));
        return new RunOutput(
            OutputMasks.MaskLog(log, root),
            PatchDump.Describe(state.PatchMod),
            ReadReports(RunSettingsValidation.ResolveReportFolder(state.OutputPath.Path, settings, []), root),
            log);
    }

    internal static IReadOnlyList<string> CaptureConsole(Action run)
    {
        var originalOut = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            run();
        }
        finally
        {
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
