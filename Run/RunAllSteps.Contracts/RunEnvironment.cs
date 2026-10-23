namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>What the entry provides to a run: the load order's facts, the factories that open its readers, the patch writing, and where the log goes.</summary>
/// <param name="Plugins">The plugin names, masters and whether each could be read.</param>
/// <param name="PluginRecords">Opens the reader of the plugin records; one reader per run.</param>
/// <param name="PatchWriting">Writes the override records into the patch.</param>
/// <param name="MeshFiles">Opens the mesh reader of the run.</param>
/// <param name="Bases">Opens the reader of the placed objects' base records over one run's record index.</param>
/// <param name="Log">Where the log sections go.</param>
/// <param name="ReportOutput">Where the report files go.</param>
/// <param name="TimeSinceStart">The time since the program started; only the entry reads the clock.</param>
public sealed record RunEnvironment(
    LoadOrderPlugins Plugins,
    IPluginRecordsFactory PluginRecords,
    IPatchWriting PatchWriting,
    IMeshFilesFactory MeshFiles,
    IBaseFactsFactory Bases,
    ILogSink Log,
    IReportOutput ReportOutput,
    Func<TimeSpan> TimeSinceStart);
