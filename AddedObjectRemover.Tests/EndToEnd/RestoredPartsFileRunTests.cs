using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs through the entry on the cleared village, where the cleaned mod's run gives back what it changed: a Standard copy inside
/// Synthesis writes the restored parts beside its patch, the next copy of the group reads them beside the patch it is handed, and a
/// Batch run or a run outside Synthesis writes no file. A chain of two copies ends with the parts one Batch run of the same configs
/// ends with.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class RestoredPartsFileRunTests : IDisposable
{
    private const int Workers = 4;
    private const string GroupName = "village";
    private const string FileNotUsedPrefix = "Restored parts file";
    private const string SeedFolder = "Seed Mod";

    private static readonly Config Village = new("village", FixtureWorld.TargetName);
    private static readonly Config City = new("city", RestoreWhereRemovedWorlds.CityPlugin);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-restored-parts-run-");
    private readonly string _dataFolder;
    private readonly IReadOnlyList<ISkyrimMod> _loadOrder;

    public RestoredPartsFileRunTests()
    {
        _dataFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Data")).FullName;
        _loadOrder = RestoreWhereRemovedWorlds.CreateVillage(_dataFolder);
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <param name="Name">The config file's name, without .json.</param>
    private sealed record Config(string Name, string Plugin);

    /// <param name="Patch">The output file the copy wrote, which the next copy of the group is handed.</param>
    private sealed record CopyRun(IReadOnlyList<string> Log, string Patch)
    {
        public string PartsFile => RestoredPartsFile.PathBeside(Patch);

        public ImmutableArray<RestoredPart> ReadParts()
        {
            var read = RestoredPartsText.Read(File.ReadAllText(PartsFile), PartsFile);
            Assert.Null(read.Problem);
            return read.Parts;
        }
    }

    [Fact]
    public void AStandardCopyInSynthesisWritesThePartsItGaveBackBesideItsPatch()
    {
        var copy = RunFirstCopy(Village);

        var parts = copy.ReadParts();

        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Equal(new PluginName(Village.Plugin), part.CleanedMod));
        Assert.DoesNotContain(copy.Log, IsFileLine);
    }

    [Fact]
    public void ARunOutsideSynthesisWritesNoRestoredPartsFile()
    {
        var folder = CreateFolder("outside");
        var state = FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(folder));

        PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => SettingsOf(Village), Workers, FixturePatcherState.WritePluginsFile(state)));

        Assert.False(File.Exists(RestoredPartsFile.PathBeside(PatchIn(folder))));
    }

    [Fact]
    public void ABatchRunWritesNoRestoredPartsFile()
    {
        var folder = CreateFolder("batch");
        BatchFiles.WriteGroup(_dataFolder, GroupName, [Village.Name, City.Name]);
        BatchFiles.WriteConfig(_dataFolder, Village.Name, SettingsOf(Village).Standard);
        BatchFiles.WriteConfig(_dataFolder, City.Name, SettingsOf(City).Standard);
        var state = FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(folder), SeedPatch());
        var settings = new Settings { Mode = PatchMode.Batch, Batch = new BatchSettings { GroupFileName = GroupName } };

        PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => settings, Workers, FixturePatcherState.WritePluginsFile(state)));

        Assert.False(File.Exists(RestoredPartsFile.PathBeside(PatchIn(folder))));
    }

    /// <summary>
    /// Only the village config gives anything back in this world. Run first, its parts must pass through the file to the end of the
    /// chain; run second, it adds them after the city copy's empty list.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheNextCopyStartsFromThePartsOfTheCopyBeforeItAndEndsWithThePartsOfOneBatchRunOfTheSameConfigs(bool cityFirst)
    {
        IReadOnlyList<Config> order = cityFirst ? [City, Village] : [Village, City];
        var first = RunFirstCopy(order[0]);
        var second = RunNextCopy(order[1], first);

        var batch = PartsOfOneBatchRun(order);

        Assert.DoesNotContain(second.Log, IsFileLine);
        Assert.NotEmpty(second.ReadParts());
        Assert.Equal(cityFirst, first.ReadParts().IsEmpty);
        Assert.Equal<RestoredPart>(first.ReadParts(), second.ReadParts().Take(first.ReadParts().Length));
        Assert.Equal<RestoredPart>(batch, second.ReadParts());
    }

    [Fact]
    public void AMissingFileBesideAPatchAnEarlierCopyWroteIsNamedInTheLogAndTheNextFileHoldsOnlyTheNewParts()
    {
        var first = RunFirstCopy(Village);
        File.Delete(first.PartsFile);

        var second = RunNextCopy(City, first);

        Assert.Equal([$"Restored parts file not found beside {first.Patch}."], second.Log.Where(IsFileLine));
        Assert.DoesNotContain(second.ReadParts(), part => part.CleanedMod == new PluginName(Village.Plugin));
    }

    [Fact]
    public void ATornFileIsNamedInTheLogAndNotUsed()
    {
        var first = RunFirstCopy(Village);
        var text = File.ReadAllText(first.PartsFile);
        File.WriteAllText(first.PartsFile, text[..^1]);

        var second = RunNextCopy(City, first);

        Assert.Equal([$"Restored parts file not used: {first.PartsFile}. Its check value does not match its content."], second.Log.Where(IsFileLine));
        Assert.DoesNotContain(second.ReadParts(), part => part.CleanedMod == new PluginName(Village.Plugin));
    }

    private static bool IsFileLine(string line) => line.StartsWith(FileNotUsedPrefix, StringComparison.Ordinal);

    /// <summary>The group's first copy, which Synthesis hands the empty seed patch; no file lies beside it.</summary>
    private CopyRun RunFirstCopy(Config config)
    {
        var folder = CreateFolder("first");
        return RunCopy(config, FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(folder), SeedPatch()));
    }

    private CopyRun RunNextCopy(Config config, CopyRun before)
    {
        var folder = CreateFolder("next");
        return RunCopy(config, FixturePatcherState.CreateAfterEarlierPatcher(_loadOrder, _dataFolder, PatchIn(folder), before.Patch, namesSource: true));
    }

    private static CopyRun RunCopy(Config config, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        var log = PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => SettingsOf(config), Workers, FixturePatcherState.WritePluginsFile(state)));
        state.PatchMod.WriteToBinary(state.OutputPath.Path);
        return new CopyRun(log, state.OutputPath.Path);
    }

    /// <summary>The configs run one after the other in one batch, which shares the parts in memory; the list it would write is recorded.</summary>
    private ImmutableArray<RestoredPart> PartsOfOneBatchRun(IReadOnlyList<Config> configs)
    {
        var state = FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("batch-in-memory")));
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var pluginRecords = new PluginRecordsFactory(world, Workers);
        ImmutableArray<RunRequest> requests = [.. configs.Select(config => RequestOf(SettingsOf(config), state, world, pluginRecords))];
        var recorded = new RecordedRestoredParts();
        var environment = new RunEnvironment(
            world.Plugins,
            pluginRecords,
            new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText),
            new MeshFilesFactory(state.DataFolderPath.Path, state.GameRelease, world.ListedPlugins),
            new BaseFactsFactory(world.Plugins.Mods),
            new ConsoleLogSink(),
            new CsvReportOutput(),
            () => TimeSpan.Zero,
            recorded);

        PipelineRun.CaptureConsole(() => StepRunner.PatchLoadOrder(requests, environment));

        return recorded.Written;
    }

    private static RunRequest RequestOf(Settings settings, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FinalWorld world, PluginRecordsFactory pluginRecords) =>
        SettingsValidator.Build(
            StandardConfig.Of(settings.Standard),
            pluginRecords.Open(),
            world.Plugins,
            settings.DetailedLogAndReports,
            settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch,
            Program.ReportsFolder(state),
            Workers);

    private static Settings SettingsOf(Config config)
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf();
        settings.Standard.CleanedMod = ModKey.FromNameAndExtension(config.Plugin);
        return settings;
    }

    private string SeedPatch() => PatchIn(Path.Combine(_root.FullName, SeedFolder));

    private string CreateFolder(string name) => Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;

    private static string PatchIn(string folder) => Path.Combine(folder, FixturePatcherState.PatchModKey.FileName);

    private sealed class RecordedRestoredParts : IRestoredPartsFile
    {
        public ImmutableArray<RestoredPart> Written { get; private set; } = [];

        public RestoredPartsRead Read() => new([], Problem: null);

        public void Write(ImmutableArray<RestoredPart> parts) => Written = parts;
    }
}
