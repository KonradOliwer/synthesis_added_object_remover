namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>What the entry provides to a run: the load order's facts, the factories that open its readers, and where the log goes.</summary>
/// <param name="Plugins">The plugin names, masters and whether each could be read.</param>
/// <param name="PluginRecords">Opens the reader and writer of the plugin records; the runner opens it once the settings allow a run.</param>
/// <param name="MeshFiles">Opens the mesh reader of the run.</param>
/// <param name="Bases">The facts of the placed objects' base records.</param>
/// <param name="Log">Where the log sections go.</param>
/// <param name="ReportOutput">Where the report files go.</param>
/// <param name="TimeSinceStart">The time since the program started; only the entry reads the clock.</param>
public sealed record RunEnvironment(
    LoadOrderPlugins Plugins,
    IPluginRecordsFactory PluginRecords,
    IMeshFilesFactory MeshFiles,
    IBaseFacts Bases,
    ILogSink Log,
    IReportOutput ReportOutput,
    Func<TimeSpan> TimeSinceStart);
