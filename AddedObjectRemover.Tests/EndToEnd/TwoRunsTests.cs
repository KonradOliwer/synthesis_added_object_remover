using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Two runs one after the other in one process share nothing: same results, and each touches only its own report folder.</summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class TwoRunsTests : IDisposable
{
    private const int Workers = 4;
    private const string StaleText = "left by an earlier run";
    private const string NotesText = "mine";
    private const string NotesFileName = "notes.txt";
    private const string ReportsPlaceholder = "<reports>";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-two-runs-");

    public static TheoryData<string> Variants => new(SettingsVariants.Names);

    public void Dispose() => _root.Delete(recursive: true);

    private (FixtureWorld World, string DataFolder) CreateWorld()
    {
        var dataFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Data")).FullName;
        return (FixtureWorld.Create(dataFolder), dataFolder);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void TwoRunsGiveTheSameLogPatchAndReportFiles(string variant)
    {
        var (world, dataFolder) = CreateWorld();
        var first = new RunSite(_root.FullName, "first");
        var second = new RunSite(_root.FullName, "second");

        var firstOutput = first.Run(world, dataFolder, variant);
        var secondOutput = second.Run(world, dataFolder, variant);

        Assert.Equal(firstOutput.Log, secondOutput.Log);
        Assert.Equal(firstOutput.Patch, secondOutput.Patch);
        Assert.Equal(firstOutput.ToExpectedOutputLines(), secondOutput.ToExpectedOutputLines());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EachRunWritesAndDeletesFilesOnlyInItsOwnReportFolder(string variant)
    {
        var (world, dataFolder) = CreateWorld();
        var first = new RunSite(_root.FullName, "first");
        var second = new RunSite(_root.FullName, "second");
        first.LeaveEarlierFiles();
        second.LeaveEarlierFiles();
        var secondBefore = second.Snapshot();

        first.Run(world, dataFolder, variant);

        Assert.Equal(secondBefore, second.Snapshot());
        Assert.True(first.EarlierFilesAreGone());
        Assert.Equal(NotesText, File.ReadAllText(first.NotesPath));

        var firstAfter = first.Snapshot();
        second.Run(world, dataFolder, variant);

        Assert.Equal(firstAfter, first.Snapshot());
        Assert.True(second.EarlierFilesAreGone());
        Assert.Equal(NotesText, File.ReadAllText(second.NotesPath));
    }

    /// <summary>One run's folders: its output plugin's folder, and the report folder inside it.</summary>
    private sealed class RunSite
    {
        private readonly string _folder;
        private readonly string _reports;

        public RunSite(string root, string name)
        {
            _folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            _reports = Path.Combine(_folder, "Reports");
        }

        public string NotesPath => Path.Combine(_reports, NotesFileName);

        private string StalePath => Path.Combine(_reports, ReportFileNames.EdgesFileName);

        public void LeaveEarlierFiles()
        {
            Directory.CreateDirectory(_reports);
            File.WriteAllText(StalePath, StaleText);
            File.WriteAllText(NotesPath, NotesText);
        }

        public bool EarlierFilesAreGone() => !File.Exists(StalePath) || File.ReadAllText(StalePath) != StaleText;

        public IReadOnlyList<string> Snapshot() =>
            Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => $"{Path.GetRelativePath(_folder, path)}: {File.ReadAllText(path)}")
                .ToList();

        public RunOutput Run(FixtureWorld world, string dataFolder, string variant)
        {
            var settings = SettingsVariants.Of(variant);
            settings.Diagnostics!.DiagnosticsFolder = _reports;
            var outputPath = Path.Combine(_folder, FixturePatcherState.PatchModKey.FileName);
            var state = FixturePatcherState.Create(world.LoadOrder, dataFolder, outputPath);
            var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
            var request = RunSettingsValidation.Build(
                settings, new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod).Open(), plugins, outputPath, Workers);
            var log = PipelineRun.CaptureConsole(() => RunAllSteps.Run(request, BuildEnvironment(state, plugins)));
            return new RunOutput(
                MaskLog(log),
                PatchDump.Describe(state.PatchMod),
                ReadReports(),
                log);
        }

        private static RunEnvironment BuildEnvironment(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, LoadOrderPlugins plugins) =>
            new(
                plugins,
                new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod),
                new MeshFilesFactory(
                    state.DataFolderPath.Path, state.GameRelease, [.. state.LoadOrder.ListedOrder.Select(listing => listing.ModKey)]),
                new BaseFactsReader(state.LinkCache),
                new ConsoleLogSink(),
                new CsvReportOutput(),
                () => TimeSpan.Zero);

        /// <summary>The run's own folder name is replaced, so a log that differs only by it is still the same log.</summary>
        private IReadOnlyList<string> MaskLog(IEnumerable<string> log) =>
            OutputMasks.MaskLog(log.Select(line => line.Replace(_reports, ReportsPlaceholder, StringComparison.OrdinalIgnoreCase)), _folder);

        private List<ReportFile> ReadReports() =>
            Directory.EnumerateFiles(_reports)
                .Where(path => Path.GetFileName(path) != NotesFileName)
                .Order(StringComparer.Ordinal)
                .Select(path => new ReportFile(
                    Path.GetFileName(path),
                    OutputMasks.MaskReport(File.ReadAllLines(path).Select(line => line.Replace(_reports, ReportsPlaceholder, StringComparison.OrdinalIgnoreCase)), _folder)))
                .ToList();
    }
}
