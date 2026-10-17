using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Runs the composition on the fixture load order, in a temporary folder of its own, and returns what it decided.</summary>
internal static class FixtureRun
{
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    public static RunOutcome Execute(StandardSettings settings, int workers, Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null) =>
        ((RunResult.Done)Run(settings, workers, new SilentLogSink(), DefaultMeshFiles, createLoadOrder: createLoadOrder)).Outcome;

    /// <param name="openMeshFiles">Creates the mesh reader's factory from the Data folder, the game release and the listed plugins.</param>
    /// <param name="wrapPlugin">Wraps the plugin records the run reads; none when the run reads them as they are.</param>
    /// <param name="wrapBases">Wraps the base facts the run reads; none when the run reads them as they are.</param>
    /// <param name="timeSinceStart">The run's clock; none for one that always says zero.</param>
    /// <param name="createLoadOrder">Builds the listed mods, lowest priority first, and writes their meshes under the Data folder; none for the fixture world.</param>
    public static RunResult Run(
        StandardSettings settings,
        int workers,
        ILogSink log,
        Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> openMeshFiles,
        Func<IPluginRecords, IPluginRecords>? wrapPlugin = null,
        Func<IBaseFacts, IBaseFacts>? wrapBases = null,
        Func<TimeSpan>? timeSinceStart = null,
        Func<string, IReadOnlyList<ISkyrimMod>>? createLoadOrder = null)
    {
        var root = Directory.CreateTempSubdirectory("aor-run-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var loadOrder = createLoadOrder is null ? FixtureWorld.Create(dataFolder).LoadOrder : createLoadOrder(dataFolder);
            var state = FixturePatcherState.Create(
                loadOrder, dataFolder, Path.Combine(root.FullName, FixturePatcherState.PatchModKey.FileName));
            var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
            var pluginRecords = new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod, plugins.Mods);
            var request = SettingsValidator.Build(StandardConfig.Of(settings), pluginRecords.Open(), plugins, Program.ReportBaseFolder(state), workers);
            var environment = new RunEnvironment(
                plugins,
                new WrappedPluginRecordsFactory(pluginRecords, wrapPlugin),
                openMeshFiles(
                    state.DataFolderPath.Path, state.GameRelease, [.. state.LoadOrder.ListedOrder.Select(listing => listing.ModKey)]),
                TestBaseFacts.Factory(state.LinkCache, wrapBases),
                log,
                new CsvReportOutput(),
                timeSinceStart ?? (() => TimeSpan.Zero));
            return Assert.Single(StepRunner.PatchLoadOrder([request], environment));
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
