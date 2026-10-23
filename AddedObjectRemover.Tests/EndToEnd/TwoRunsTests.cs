using System.Globalization;
using System.Text.RegularExpressions;
using AddedObjectRemover.Tests.Fixtures;
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

    /// <summary>The first run follows up on its own removals in the same pass, so what it kept is still kept when the second run judges it again.</summary>
    private const int RemovedByTheSecondRun = 0;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-two-runs-");

    public static TheoryData<string> Variants => new(SettingsVariants.Names);

    public static TheoryData<string> VariantsWritingReports =>
        new(SettingsVariants.Names.Where(name => SettingsVariants.Of(name).DetailedLogAndReports));

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
    [MemberData(nameof(VariantsWritingReports))]
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

    /// <summary>
    /// The second run's link cache is its own, not the first run's, which indexed its record types before the first run wrote;
    /// so the second run reads the first run's removals from the patch.
    /// </summary>
    [Fact]
    public void EachRunOfABatchOpensItsOwnRecordIndexAndItsBaseFactsOverItAndReadsWhatTheRunsBeforeItWrote()
    {
        var (world, dataFolder) = CreateWorld();
        var outputPath = Path.Combine(_root.FullName, FixturePatcherState.PatchModKey.FileName);
        var state = FixturePatcherState.Create(world.LoadOrder, dataFolder, outputPath);
        using var finalWorld = FixturePatcherState.CreateFinalWorld(state);
        var settings = SettingsVariants.Of(SettingsVariants.Support);
        var request = SettingsValidator.Build(
            StandardConfig.Of(settings.Standard),
            new PluginRecordsFactory(finalWorld, Workers).Open(),
            finalWorld.Plugins,
            settings.DetailedLogAndReports,
            settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch,
            Program.ReportsFolder(state),
            Workers);
        var pluginRecords = new RecordingPluginRecordsFactory(new PluginRecordsFactory(finalWorld, Workers));
        var bases = new RecordingBaseFactsFactory(new BaseFactsFactory(finalWorld.Plugins.Mods));
        var environment = BuildEnvironment(state, finalWorld, pluginRecords, bases);

        var log = PipelineRun.CaptureConsole(() => StepRunner.PatchLoadOrder([request, request], environment));

        Assert.Equal(2, bases.Indexes.Count);
        Assert.NotSame(bases.Indexes[0], bases.Indexes[1]);
        Assert.Equal(pluginRecords.Opened.Select(opened => opened.ReadRecordIndex()), bases.Indexes);
        var runs = RunSummariesOf(log);
        Assert.True(runs[0].Removed > 0);
        Assert.Equal(runs[0].Removed, runs[1].AlreadyRemoved);
    }

    /// <summary>A second run of the same cleaned mod reads the first run's removals from the patch: they count as already removed and are not judged again.</summary>
    [Fact]
    public void ASecondRunOfTheSameCleanedModStartsFromTheRemovalsTheFirstRunWroteIntoThePatch()
    {
        var (world, dataFolder) = CreateWorld();
        var state = FixturePatcherState.Create(world.LoadOrder, dataFolder, Path.Combine(_root.FullName, FixturePatcherState.PatchModKey.FileName));
        using var finalWorld = FixturePatcherState.CreateFinalWorld(state);
        var settings = SettingsVariants.Of(SettingsVariants.Support);
        var pluginRecords = new PluginRecordsFactory(finalWorld, Workers);
        var request = SettingsValidator.Build(
            StandardConfig.Of(settings.Standard),
            pluginRecords.Open(),
            finalWorld.Plugins,
            settings.DetailedLogAndReports,
            settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch,
            Program.ReportsFolder(state),
            Workers);
        var environment = BuildEnvironment(state, finalWorld, pluginRecords, new BaseFactsFactory(finalWorld.Plugins.Mods));

        var log = PipelineRun.CaptureConsole(() => StepRunner.PatchLoadOrder([request, request], environment));

        var runs = RunSummariesOf(log);
        Assert.True(runs[0].Removed > 0);
        Assert.Equal(
            new RunSummary(runs[0].Objects, AlreadyRemoved: runs[0].Removed, Judged: runs[0].Judged - runs[0].Removed, Removed: RemovedByTheSecondRun),
            runs[1]);
    }

    /// <returns>The summary of each run of the log, in run order; both runs of a two-run log have one.</returns>
    private static List<RunSummary> RunSummariesOf(IEnumerable<string> log)
    {
        var runs = log.Select(line => RunSummaryLine().Match(line)).Where(match => match.Success).Select(RunSummary.Of).ToList();
        Assert.Equal(2, runs.Count);
        return runs;
    }

    private static Regex RunSummaryLine() =>
        new(@"^Objects of \S+: (\d+); already removed by earlier patchers in this group: (\d+); judged in this run: (\d+); removed in this run: (\d+)\.$");

    private sealed record RunSummary(int Objects, int AlreadyRemoved, int Judged, int Removed)
    {
        public static RunSummary Of(Match match) =>
            new(Number(match, 1), Number(match, 2), Number(match, 3), Number(match, 4));

        private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    }

    private static RunEnvironment BuildEnvironment(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FinalWorld world, IPluginRecordsFactory pluginRecords, IBaseFactsFactory bases) =>
        new(
            world.Plugins,
            pluginRecords,
            new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod),
            new MeshFilesFactory(state.DataFolderPath.Path, state.GameRelease, [.. world.Order.ListedOrder.Select(listing => listing.ModKey)]),
            bases,
            new ConsoleLogSink(),
            new CsvReportOutput(),
            () => TimeSpan.Zero);

    private sealed class RecordingPluginRecordsFactory(IPluginRecordsFactory inner) : IPluginRecordsFactory
    {
        public List<IPluginRecords> Opened { get; } = [];

        public IPluginRecords Open()
        {
            var opened = inner.Open();
            Opened.Add(opened);
            return opened;
        }
    }

    private sealed class RecordingBaseFactsFactory(IBaseFactsFactory inner) : IBaseFactsFactory
    {
        public List<IRecordIndex> Indexes { get; } = [];

        public IBaseFacts Open(IRecordIndex records)
        {
            Indexes.Add(records);
            return inner.Open(records);
        }
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
            var outputPath = Path.Combine(_folder, FixturePatcherState.PatchModKey.FileName);
            var state = FixturePatcherState.Create(world.LoadOrder, dataFolder, outputPath);
            using var finalWorld = FixturePatcherState.CreateFinalWorld(state);
            var pluginRecords = new PluginRecordsFactory(finalWorld, Workers);
            var request = SettingsValidator.Build(
                StandardConfig.Of(settings.Standard),
                pluginRecords.Open(),
                finalWorld.Plugins,
                settings.DetailedLogAndReports,
                settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch,
                Program.ReportsFolder(state),
                Workers);
            var environment = BuildEnvironment(state, finalWorld, pluginRecords, new BaseFactsFactory(finalWorld.Plugins.Mods));
            var log = PipelineRun.CaptureConsole(() => StepRunner.PatchLoadOrder([request], environment));
            return new RunOutput(
                MaskLog(log),
                PatchDump.Describe(state.PatchMod),
                ReadReports(),
                log);
        }

        /// <summary>The run's own folder name is replaced, so a log that differs only by it is still the same log.</summary>
        private IReadOnlyList<string> MaskLog(IEnumerable<string> log) =>
            OutputMasks.MaskLog(log.Select(line => line.Replace(_reports, ReportsPlaceholder, StringComparison.OrdinalIgnoreCase)), _folder);

        private List<ReportFile> ReadReports() =>
            Directory.Exists(_reports)
                ? Directory.EnumerateFiles(_reports)
                    .Where(path => Path.GetFileName(path) != NotesFileName)
                    .Order(StringComparer.Ordinal)
                    .Select(path => new ReportFile(
                        Path.GetFileName(path),
                        OutputMasks.MaskReport(File.ReadAllLines(path).Select(line => line.Replace(_reports, ReportsPlaceholder, StringComparison.OrdinalIgnoreCase)), _folder)))
                    .ToList()
                : [];
    }
}
