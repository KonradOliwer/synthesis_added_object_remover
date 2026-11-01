using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.WriteThePatch;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>
/// Runs the patch runs of a batch one after the other. Each run creates its own caches, runs the steps in order and
/// hands each step's result to the log and reports as the step ends, printing the sections it returns; then it
/// writes the report files. All decisions of a run are made before its patch is written. Nothing passes between runs: a
/// run decides from the world, which includes what earlier runs wrote into the patch.
/// </summary>
public static partial class StepRunner
{
    public static ImmutableArray<RunResult> PatchLoadOrder(ImmutableArray<RunRequest> requests, RunEnvironment environment)
    {
        MarkPatchWhenARunWillWrite(requests, environment);
        var results = ImmutableArray.CreateBuilder<RunResult>(requests.Length);
        foreach (var request in requests) results.Add(PatchOneRun(request, environment));
        return results.MoveToImmutable();
    }

    /// <remarks>A batch of stop requests leaves the patch untouched, so it gets no marker.</remarks>
    private static void MarkPatchWhenARunWillWrite(ImmutableArray<RunRequest> requests, RunEnvironment environment)
    {
        if (requests.Any(request => request is RunRequest.Ready)) environment.PatchWriting.WritePatchMarker();
    }

    /// <remarks>The start sections of a stop request already hold its stop line.</remarks>
    private static RunResult PatchOneRun(RunRequest request, RunEnvironment environment)
    {
        Print(environment.Log, LogAndReports.BuildStartSections(request));
        return request switch
        {
            RunRequest.Ready ready => PatchWithSettings(ready, environment),
            RunRequest.Stop stop => new RunResult.Stopped(stop.Reason),
            _ => throw new UnreachableException(),
        };
    }

    private static RunResult PatchWithSettings(RunRequest.Ready ready, RunEnvironment environment)
    {
        var scope = SetUpRun(ready.Settings, ready.Source, environment);
        return CollectDecideAndWrite(scope);
    }

    private static RunScope SetUpRun(RunSettings settings, SettingsSource source, RunEnvironment environment)
    {
        var unexpectedErrors = new UnexpectedErrors();
        var reportFolder = PrepareReportFolder(settings, environment);
        var plugin = environment.PluginRecords.Open();
        var records = plugin.ReadRecordIndex();
        var loadOrderCaches = LoadOrderCaches.Create(
            environment.MeshFiles,
            unexpectedErrors,
            plugin,
            environment.Bases.Open(records),
            environment.Bases.ReadCollisionLayers(records),
            TooCloseObjects.NpcBodyRules,
            TooCloseObjects.PlacedNpcRules,
            RestingObjects.SurfaceSamplesPerMesh);
        var report = new LogAndReports(settings, reportFolder, loadOrderCaches, unexpectedErrors);
        return new RunScope(settings, source, environment, plugin, loadOrderCaches, unexpectedErrors, report, reportFolder);
    }

    private static ReportFolderPrepared PrepareReportFolder(RunSettings settings, RunEnvironment environment)
    {
        var reportFolder = environment.ReportOutput.Prepare(settings.Reports);
        Print(environment.Log, LogAndReports.BuildReportFolderSections(reportFolder));
        return reportFolder;
    }

    private static RunResult CollectDecideAndWrite(RunScope scope)
    {
        PrepareMeshReading(scope);
        var trust = ClassifyModTrust(scope);
        var collected = CollectPlacedObjects(scope, trust);
        if (collected.Stop is { } stop)
        {
            // A stopped run writes no report files and no records.
            return StopAfterStep(scope.Environment.Log, stop, scope.Source);
        }

        var runCaches = AddObjectCaches(scope, collected);
        var decided = Decide(scope, runCaches, trust, collected);
        var written = WritePatch(scope, decided);
        return FinishReports(scope, decided, written);
    }

    private static void Print(ILogSink log, ImmutableArray<LogSection> sections)
    {
        foreach (var section in sections) log.Print(section);
    }

    private static RunResult.Stopped StopAfterStep(ILogSink log, StopReason reason, SettingsSource source)
    {
        Print(log, LogAndReports.BuildStopSections(reason, source));
        return new RunResult.Stopped(reason);
    }

    /// <remarks>Timed through the run's clock, since the log times only work that returns a result.</remarks>
    private static void PrepareMeshReading(RunScope scope)
    {
        var clock = scope.Environment.TimeSinceStart;
        var start = clock();
        scope.LoadOrderCaches.PrepareMeshReading();
        Print(scope.Environment.Log, scope.Report.BuildArchiveSections(clock() - start));
    }

    private static ModTrustTable ClassifyModTrust(RunScope scope) =>
        scope.Environment.Log.Timed(
            () => ModTrustClassifier.Classify(new(scope.Environment.Plugins, scope.Settings.ModTrust)),
            (trust, elapsed) => scope.Report.BuildStepSections(trust, scope.Environment.Plugins, elapsed));

    /// <remarks>The step times its two parts through the run's clock, since they print one section.</remarks>
    private static CollectedObjects CollectPlacedObjects(RunScope scope, ModTrustTable trust)
    {
        var collected = PlacedObjectCollector.Collect(new(scope.Plugin, trust, trust.AcceptedWorld, scope.Settings.Read, scope.Environment.TimeSinceStart));
        Print(scope.Environment.Log, scope.Report.BuildStepSections(collected.Objects, collected.ScanTime, collected.LinkReadTime));
        return collected.Objects;
    }

    private static IRunCaches AddObjectCaches(RunScope scope, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => scope.LoadOrderCaches.AddObjectCaches(
                collected,
                RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors),
                scope.Settings.Execution),
            (_, elapsed) => scope.Report.BuildIndexSections(collected, elapsed));

    private static WriteSummary WritePatch(RunScope scope, StepResults decided) =>
        scope.Environment.Log.Timed(
            () => PatchWriter.Write(
                new(decided.Final, decided.Collected.Targets, decided.OtherModsObjects, decided.RestoreWhereRemoved, scope.Plugin, scope.Environment.PatchWriting)),
            scope.Report.BuildStepSections);

    private static RunResult FinishReports(RunScope scope, StepResults decided, WriteSummary written)
    {
        var log = scope.Environment.Log;
        var ended = scope.Report.BuildEndOfRun(decided, written);
        Print(log, ended.Sections);
        var files = scope.Environment.ReportOutput.Write(scope.Settings.Reports, ended.Tables);
        Print(log, scope.Report.BuildFilesWrittenSections(ended, files, scope.Environment.TimeSinceStart()));
        return new RunResult.Done(ended.Outcome);
    }

    /// <summary>What every phase method reads besides its own inputs.</summary>
    /// <param name="ReportFolder">What the report writer prepared, which says whether the report files are written.</param>
    private sealed record RunScope(
        RunSettings Settings,
        SettingsSource Source,
        RunEnvironment Environment,
        IPluginRecords Plugin,
        LoadOrderCaches LoadOrderCaches,
        IUnexpectedErrors UnexpectedErrors,
        LogAndReports Report,
        ReportFolderPrepared ReportFolder);
}
