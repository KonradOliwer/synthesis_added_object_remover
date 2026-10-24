using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Runs the composition on the fixture load order, in a temporary folder of its own, and returns what it decided.</summary>
internal static class FixtureRun
{
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    public static RunOutcome Execute(Settings settings, int workers,Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null) =>
        ((RunResult.Done)Run(settings, workers, new SilentLogSink(), DefaultMeshFiles, createLoadOrder: createLoadOrder)).Outcome;

    /// <param name="openMeshFiles">Creates the mesh reader's factory from the Data folder, the game release and the listed plugins.</param>
    /// <param name="wrapPlugin">Wraps the plugin records the run reads; none when the run reads them as they are.</param>
    /// <param name="wrapBases">Wraps the base facts the run reads; none when the run reads them as they are.</param>
    /// <param name="timeSinceStart">The run's clock; none for one that always says zero.</param>
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    /// <param name="patchPosition">How many of the listed mods load above the patch; the others load below it. None puts the patch last.</param>
    /// <param name="inspectPatch">Reads the patch once the run is over.</param>
    public static RunResult Run(
        Settings settings,
        int workers,
        ILogSink log,
        Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> openMeshFiles,
        Func<IPluginRecords, IPluginRecords>? wrapPlugin = null,
        Func<IBaseFacts, IBaseFacts>? wrapBases = null,
        Func<TimeSpan>? timeSinceStart = null,
        Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null,
        int? patchPosition = null,
        Action<ISkyrimModGetter>? inspectPatch = null)
    {
        var root = Directory.CreateTempSubdirectory("aor-run-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var listedMods = createLoadOrder is null ? FixtureWorld.Create(dataFolder).LoadOrder : createLoadOrder(dataFolder);
            var abovePatch = patchPosition ?? listedMods.Count;
            var state = FixturePatcherState.Create(
                [.. listedMods.Take(abovePatch)], dataFolder, Path.Combine(root.FullName, FixturePatcherState.PatchModKey.FileName));
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
                new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod),
                openMeshFiles(state.DataFolderPath.Path, state.GameRelease, [.. world.Order.ListedOrder.Select(listing => listing.ModKey)]),
                new WrappedBaseFactsFactory(new BaseFactsFactory(world.Plugins.Mods), wrapBases),
                log,
                new CsvReportOutput(),
                timeSinceStart ?? (() => TimeSpan.Zero),
                new UnreadableRecordsReported());
            var result = Assert.Single(StepRunner.PatchLoadOrder([request], environment));
            inspectPatch?.Invoke(state.PatchMod);
            return result;
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

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
