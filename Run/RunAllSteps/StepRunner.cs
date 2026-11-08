using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Run.RunAllSteps.RunCaches;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.WriteThePatch;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>
/// Runs the patch runs of a batch one after the other. Each run creates its own caches, runs the steps in order and
/// hands each step's result to the log and reports as the step ends, printing the sections it returns; then it
/// writes the report files. All decisions of a run are made before its patch is written. A run decides from the world, which
/// includes what earlier runs wrote into the patch; besides the patch, only the list of restored parts passes from run to run,
/// starting from what an earlier copy of this patcher left in the restored parts file and ending in that file.
/// </summary>
public static partial class StepRunner
{
    public static ImmutableArray<RunResult> PatchLoadOrder(ImmutableArray<RunRequest> requests, RunEnvironment environment)
    {
        var restored = ReadRestoredParts(environment);
        MarkPatchWhenARunWillWrite(requests, environment);
        var results = ImmutableArray.CreateBuilder<RunResult>(requests.Length);
        foreach (var request in requests)
        {
            var ended = PatchOneRun(request, environment, restored);
            results.Add(ended.Result);
            restored = ended.Restored;
        }

        environment.RestoredPartsFile.Write(restored);
        return results.MoveToImmutable();
    }

    private static ImmutableArray<RestoredPart> ReadRestoredParts(RunEnvironment environment)
    {
        var read = environment.RestoredPartsFile.Read();
        Print(environment.Log, LogAndReports.BuildRestoredPartsFileSections(read.Problem));
        return read.Parts;
    }

    /// <remarks>A batch of stop requests leaves the patch untouched, so it gets no marker.</remarks>
    private static void MarkPatchWhenARunWillWrite(ImmutableArray<RunRequest> requests, RunEnvironment environment)
    {
        if (requests.Any(request => request is RunRequest.Ready)) environment.PatchWriting.WritePatchMarker();
    }

    /// <remarks>The start sections of a stop request already hold its stop line.</remarks>
    /// <param name="restoredBefore">The restored parts known when the run starts.</param>
    private static RunEnd PatchOneRun(RunRequest request, RunEnvironment environment, ImmutableArray<RestoredPart> restoredBefore)
    {
        Print(environment.Log, LogAndReports.BuildStartSections(request));
        return request switch
        {
            RunRequest.Ready ready => PatchWithSettings(ready, environment, restoredBefore),
            RunRequest.Stop stop => new RunEnd(new RunResult.Stopped(stop.Reason), restoredBefore),
            _ => throw new UnreachableException(),
        };
    }

    private static RunEnd PatchWithSettings(RunRequest.Ready ready, RunEnvironment environment, ImmutableArray<RestoredPart> restoredBefore)
    {
        var scope = SetUpRun(ready.Settings, ready.Source, environment, restoredBefore);
        return CollectDecideAndWrite(scope);
    }

    private static RunScope SetUpRun(RunSettings settings, SettingsSource source, RunEnvironment environment, ImmutableArray<RestoredPart> restoredBefore)
    {
        var unexpectedErrors = new UnexpectedErrors();
        var reportFolder = PrepareReportFolder(settings, environment);
        var plugin = environment.PluginRecords.Open();
        var records = plugin.ReadRecordIndex();
        var npcCacheRules = TooCloseRules.CreateNpcCacheRules();
        var loadOrderCaches = LoadOrderCaches.Create(
            environment.MeshFiles,
            plugin,
            environment.Bases.Open(records),
            () => environment.Bases.ReadCollisionLayers(records),
            npcCacheRules.Bodies,
            npcCacheRules.PlacedNpcs,
            RestingObjectsRules.SurfaceSamplesPerMesh());
        var report = new LogAndReports(settings, reportFolder, loadOrderCaches, unexpectedErrors);
        return new RunScope(settings, source, environment, plugin, loadOrderCaches, unexpectedErrors, report, reportFolder, restoredBefore);
    }

    private static ReportFolderPrepared PrepareReportFolder(RunSettings settings, RunEnvironment environment)
    {
        var reportFolder = environment.ReportOutput.Prepare(settings.Reports);
        Print(environment.Log, LogAndReports.BuildReportFolderSections(reportFolder));
        return reportFolder;
    }

    private static RunEnd CollectDecideAndWrite(RunScope scope)
    {
        PrepareMeshReading(scope);
        var trust = ClassifyModTrust(scope);
        var collected = CollectPlacedObjects(scope, trust);
        if (collected.Stop is { } stop)
        {
            // A stopped run writes no report files and no records.
            return new RunEnd(StopAfterStep(scope.Environment.Log, stop, scope.Source), scope.RestoredBefore);
        }

        var runCaches = AddObjectCaches(scope, collected);
        var decided = Decide(scope, runCaches, trust, collected);
        var written = WritePatch(scope, decided, trust.CleanedMod);
        return new RunEnd(FinishReports(scope, decided, written), [.. scope.RestoredBefore, .. written.RestoredParts]);
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
        var elapsed = Timed.Run(scope.Environment.TimeSinceStart, scope.LoadOrderCaches.PrepareMeshReading);
        Print(scope.Environment.Log, scope.Report.BuildArchiveSections(elapsed));
    }

    private static ModTrustTable ClassifyModTrust(RunScope scope) =>
        scope.Environment.Log.Timed(
            () => ModTrustClassifier.Classify(new(scope.Environment.Plugins, scope.Settings.ModTrust)),
            (trust, elapsed) => scope.Report.BuildStepSections(trust, scope.Environment.Plugins, elapsed));

    /// <remarks>The step times its two parts through the run's clock, since they print one section.</remarks>
    private static CollectedObjects CollectPlacedObjects(RunScope scope, ModTrustTable trust)
    {
        var collected = PlacedObjectCollector.Collect(
            new(
                scope.Plugin,
                trust,
                trust.AcceptedWorld,
                scope.Settings.Read,
                RemovalCauses.CreateOtherRecordChanges(scope.RestoredBefore),
                scope.Environment.TimeSinceStart));
        Print(scope.Environment.Log, scope.Report.BuildStepSections(collected.Objects, collected.ScanTime, collected.LinkReadTime));
        return collected.Objects;
    }

    private static IRunCaches AddObjectCaches(RunScope scope, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => scope.LoadOrderCaches.AddObjectCaches(collected, CreateMeasurementRules(scope), scope.Settings.Execution),
            (_, elapsed) => scope.Report.BuildIndexSections(collected, elapsed));

    private static WriteSummary WritePatch(RunScope scope, StepResults decided, PluginName cleanedMod) =>
        scope.Environment.Log.Timed(
            () => PatchWriter.Write(
                new(decided.Final, decided.Collected.Targets, decided.OtherModsObjects, decided.RestoreWhereRemoved, scope.Plugin, scope.Environment.PatchWriting, cleanedMod)),
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
    /// <param name="RestoredBefore">The restored parts known when the run starts.</param>
    private sealed record RunScope(
        RunSettings Settings,
        SettingsSource Source,
        RunEnvironment Environment,
        IPluginRecords Plugin,
        LoadOrderCaches LoadOrderCaches,
        IUnexpectedErrors UnexpectedErrors,
        LogAndReports Report,
        ReportFolderPrepared ReportFolder,
        ImmutableArray<RestoredPart> RestoredBefore);

    /// <param name="Restored">The restored parts known when the run ends: those known at its start, then those it gave back.</param>
    private sealed record RunEnd(RunResult Result, ImmutableArray<RestoredPart> Restored);
}
