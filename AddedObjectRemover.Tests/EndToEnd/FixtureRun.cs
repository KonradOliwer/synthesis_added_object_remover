using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Runs the composition on the fixture load order, in a temporary folder of its own, and returns what it decided.</summary>
internal static class FixtureRun
{
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    /// <param name="inspectPatch">Reads the patch once the run is over.</param>
    /// <param name="earlierPatchFile">The output file of the patcher before this run in the group, imported as the patch it adds to; none when the patch starts empty.</param>
    public static RunOutcome Execute(
        Settings settings, int workers, Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null, Action<ISkyrimModGetter>? inspectPatch = null, string? earlierPatchFile = null) =>
        ((RunResult.Done)Attempt(settings, workers, createLoadOrder, inspectPatch, earlierPatchFile)).Outcome;

    /// <summary>As <see cref="Execute"/>, for a run that may stop before it decides anything.</summary>
    /// <param name="restoredBefore">The parts the patcher before this run in the group gave back, as its restored parts file hands them on; none when the patch starts empty.</param>
    public static RunResult Attempt(
        Settings settings,
        int workers,
        Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null,
        Action<ISkyrimModGetter>? inspectPatch = null,
        string? earlierPatchFile = null,
        ImmutableArray<RestoredPart> restoredBefore = default) =>
        Run(settings, workers, new SilentLogSink(), DefaultMeshFiles, createLoadOrder: createLoadOrder, inspectPatch: inspectPatch, earlierPatchFile: earlierPatchFile, restoredBefore: restoredBefore);

    /// <param name="openMeshFiles">Creates the mesh reader's factory from the Data folder, the game release and the listed plugins.</param>
    /// <param name="wrapPlugin">Wraps the plugin records the run reads; none when the run reads them as they are.</param>
    /// <param name="wrapBases">Wraps the base facts the run reads; none when the run reads them as they are.</param>
    /// <param name="timeSinceStart">The run's clock; none for one that always says zero.</param>
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    /// <param name="patchPosition">How many of the listed mods load above the patch; the others load below it. None puts the patch last.</param>
    /// <param name="inspectPatch">Reads the patch once the run is over.</param>
    /// <param name="inspectRunFolder">Reads the folder the run's output and reports folder are in once the run is over; the report folder is in it.</param>
    /// <param name="earlierPatcherWrites">What an earlier patcher of the group wrote into the patch before the run starts; none when the patch starts empty.</param>
    /// <param name="earlierPatchFile">The output file of the patcher before this run in the group, imported as the patch it adds to; none when the patch starts empty.</param>
    /// <param name="restoredBefore">The parts the patcher before this run in the group gave back, as its restored parts file hands them on; none when the patch starts empty.</param>
    public static RunResult Run(
        Settings settings,
        int workers,
        ILogSink log,
        Func<string, GameRelease, IReadOnlyList<PluginName>, IMeshFilesFactory> openMeshFiles,
        Func<IPluginRecords, IPluginRecords>? wrapPlugin = null,
        Func<IBaseFacts, IBaseFacts>? wrapBases = null,
        Func<TimeSpan>? timeSinceStart = null,
        Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null,
        int? patchPosition = null,
        Action<ISkyrimModGetter>? inspectPatch = null,
        Action<string>? inspectRunFolder = null,
        Action<IPatcherState<ISkyrimMod, ISkyrimModGetter>>? earlierPatcherWrites = null,
        string? earlierPatchFile = null,
        ImmutableArray<RestoredPart> restoredBefore = default)
    {
        var root = Directory.CreateTempSubdirectory("aor-run-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var listedMods = createLoadOrder is null ? FixtureWorld.Create(dataFolder).LoadOrder : createLoadOrder(dataFolder);
            var abovePatch = patchPosition ?? listedMods.Count;
            var outputPath = Path.Combine(root.FullName, FixturePatcherState.PatchModKey.FileName);
            var state = earlierPatchFile is null
                ? FixturePatcherState.Create([.. listedMods.Take(abovePatch)], dataFolder, outputPath)
                : FixturePatcherState.CreateAfterEarlierPatcher([.. listedMods.Take(abovePatch)], dataFolder, outputPath, earlierPatchFile);
            earlierPatcherWrites?.Invoke(state);
            using var world = FixturePatcherState.CreateFinalWorld(state, [.. listedMods.Skip(abovePatch)]);
            var pluginRecords = new PluginRecordsFactory(world, workers);
            var request = SettingsValidator.Build(
                StandardConfig.Of(settings.Standard),
                pluginRecords.Open(),
                world.Plugins,
                settings.DetailedLogAndReports,
                settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch,
                Program.ReportsFolder(state),
                workers);
            var environment = new RunEnvironment(
                world.Plugins,
                new WrappedPluginRecordsFactory(pluginRecords, wrapPlugin),
                new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText),
                openMeshFiles(state.DataFolderPath.Path, state.GameRelease, world.ListedPlugins),
                new WrappedBaseFactsFactory(new BaseFactsFactory(world.Plugins.Mods), wrapBases),
                log,
                new CsvReportOutput(),
                timeSinceStart ?? (() => TimeSpan.Zero),
                new HandedOnRestoredParts(restoredBefore.IsDefault ? [] : restoredBefore));
            var result = Assert.Single(StepRunner.PatchLoadOrder([request], environment));
            inspectPatch?.Invoke(state.PatchMod);
            inspectRunFolder?.Invoke(root.FullName);
            return result;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>Runs with the log recorded and the reports read, and returns what the run printed, patched and reported, masked like the output of a whole run through the entry.</summary>
    /// <param name="earlierPatcherWrites">What an earlier patcher of the group wrote into the patch before the run starts; none when the patch starts empty.</param>
    /// <param name="describeMore">More lines about the patch than its placed records, such as its cells, land and navmeshes; none for the placed records only.</param>
    public static (RunOutcome Outcome, RunOutput Output) Capture(
        Settings settings,
        int workers,
        Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder,
        Action<IPatcherState<ISkyrimMod, ISkyrimModGetter>>? earlierPatcherWrites = null,
        Func<ISkyrimModGetter, IReadOnlyList<string>>? describeMore = null)
    {
        var log = new RecordingLogSink();
        IReadOnlyList<string> patch = [];
        IReadOnlyList<ReportFile> reports = [];
        var runFolder = string.Empty;
        var result = Run(
            settings,
            workers,
            log,
            DefaultMeshFiles,
            createLoadOrder: createLoadOrder,
            inspectPatch: mod => patch = [.. PatchDump.Describe(mod), .. describeMore?.Invoke(mod) ?? []],
            inspectRunFolder: folder =>
            {
                runFolder = folder;
                reports = PipelineRun.ReadReports(Path.Combine(folder, Program.ReportsFolderName), folder);
            },
            earlierPatcherWrites: earlierPatcherWrites);
        var printed = log.Sections.SelectMany(section => section.Lines).ToList();
        return (((RunResult.Done)result).Outcome, new RunOutput(OutputMasks.MaskLog(printed, runFolder), patch, reports, printed));
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<PluginName> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    /// <summary>Hands the run the parts of the patcher before it and keeps none of what it writes.</summary>
    private sealed class HandedOnRestoredParts(ImmutableArray<RestoredPart> parts) : IRestoredPartsFile
    {
        public RestoredPartsRead Read() => new(parts, Problem: null);

        public void Write(ImmutableArray<RestoredPart> written)
        {
        }
    }

    private sealed class WrappedPluginRecordsFactory(
        IPluginRecordsFactory inner, Func<IPluginRecords, IPluginRecords>? wrap) : IPluginRecordsFactory
    {
        public IPluginRecords Open()
        {
            var opened = inner.Open();
            return wrap is null ? opened : wrap(opened);
        }
    }

    private sealed class WrappedBaseFactsFactory(IBaseFactsFactory inner, Func<IBaseFacts, IBaseFacts>? wrap) : IBaseFactsFactory
    {
        public IBaseFacts Open(IRecordIndex records)
        {
            var opened = inner.Open(records);
            return wrap is null ? opened : wrap(opened);
        }

        public IReadOnlyDictionary<uint, CollisionLayerFact> ReadCollisionLayers(IRecordIndex records) => inner.ReadCollisionLayers(records);
    }

    /// <remarks>Builds the sections it does not print, because building them takes the problems and counts what the log shows, as a printing run does.</remarks>
    private sealed class SilentLogSink : ILogSink
    {
        public void Print(LogSection section)
        {
        }

        public T Timed<T>(Func<T> work, Func<T, TimeSpan, System.Collections.Immutable.ImmutableArray<LogSection>> sections)
        {
            var result = work();
            sections(result, TimeSpan.Zero);
            return result;
        }
    }
}
