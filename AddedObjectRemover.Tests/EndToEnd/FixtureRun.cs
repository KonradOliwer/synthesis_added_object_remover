using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Runs the composition on the fixture load order, in a temporary folder of its own, and returns what it decided.</summary>
internal static class FixtureRun
{
    public static RunOutcome Execute(Settings settings, int workers) =>
        ((RunResult.Done)Run(settings, workers, new SilentLogSink(), DefaultMeshFiles)).Outcome;

    /// <param name="openMeshFiles">Creates the mesh reader's factory from the Data folder, the game release and the listed plugins.</param>
    /// <param name="wrapPlugin">Wraps the plugin records the run reads; none when the run reads them as they are.</param>
    /// <param name="wrapBases">Wraps the base facts the run reads; none when the run reads them as they are.</param>
    public static RunResult Run(
        Settings settings,
        int workers,
        ILogSink log,
        Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> openMeshFiles,
        Func<IPluginRecords, IPluginRecords>? wrapPlugin = null,
        Func<IBaseFacts, IBaseFacts>? wrapBases = null)
    {
        var root = Directory.CreateTempSubdirectory("aor-run-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var world = FixtureWorld.Create(dataFolder);
            var state = FixturePatcherState.Create(
                world.LoadOrder, dataFolder, Path.Combine(root.FullName, FixturePatcherState.PatchModKey.FileName));
            var plugins = LoadOrderPluginsReader.Read(state.LoadOrder, state.PatchMod.ModKey);
            var pluginRecords = new PluginRecordsFactory(state.LoadOrder, state.LinkCache, state.PatchMod);
            var request = RunSettingsValidation.Build(settings, pluginRecords.Open(), plugins, state.OutputPath.Path, workers);
            var environment = new RunEnvironment(
                plugins,
                new WrappedPluginRecordsFactory(pluginRecords, wrapPlugin),
                openMeshFiles(
                    state.DataFolderPath.Path, state.GameRelease, [.. state.LoadOrder.ListedOrder.Select(listing => listing.ModKey)]),
                wrapBases is null ? new BaseFactsReader(state.LinkCache) : wrapBases(new BaseFactsReader(state.LinkCache)),
                log,
                new CsvReportOutput(),
                () => TimeSpan.Zero);
            return RunAllSteps.Run(request, environment);
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

    private sealed class SilentLogSink : ILogSink
    {
        public void Print(LogSection section)
        {
        }

        public T Timed<T>(Func<T> work, Func<T, TimeSpan, LogSection> section) => work();

        public T Measured<T>(Func<T> work, out TimeSpan elapsed)
        {
            elapsed = TimeSpan.Zero;
            return work();
        }
    }
}
