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
using AddedObjectRemover.Steps.FindTargetObjectsToKeep;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>
/// Runs the patch runs of a batch one after the other. Each run creates its own caches, runs the steps in order and
/// hands each step's result to the log and reports as the step ends, printing the sections it returns; then it
/// writes the report files. All decisions of a run are made before its patch is written. Nothing passes between runs: a
/// run decides from the world, which includes what earlier runs wrote into the patch. Only the environment's record of
/// the unreadable records the log has named passes between the logs of the runs.
/// </summary>
public static class StepRunner
{
    public static ImmutableArray<RunResult> PatchLoadOrder(ImmutableArray<RunRequest> requests, RunEnvironment environment)
    {
        var results = ImmutableArray.CreateBuilder<RunResult>(requests.Length);
        foreach (var request in requests) results.Add(PatchOneRun(request, environment));
        return results.MoveToImmutable();
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
        var bases = environment.Bases.Open(plugin.ReadRecordIndex());
        var loadOrderCaches = LoadOrderCaches.Create(
            environment.MeshFiles,
            unexpectedErrors,
            plugin,
            bases,
            TooCloseObjects.NpcBodyRules,
            TooCloseObjects.PlacedNpcRules);
        var report = new LogAndReports(settings, reportFolder, loadOrderCaches, unexpectedErrors, environment.UnreadableRecordsReported);
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

    private static void PrepareMeshReading(RunScope scope)
    {
        scope.LoadOrderCaches.PrepareMeshReading();
        Print(scope.Environment.Log, scope.Report.BuildArchiveSections());
    }

    private static ModTrustTable ClassifyModTrust(RunScope scope)
    {
        var trust = ModTrustClassifier.Classify(scope.Environment.Plugins, scope.Settings.ModTrust);
        Print(scope.Environment.Log, scope.Report.BuildStepSections(trust, scope.Environment.Plugins));
        return trust;
    }

    private static CollectedObjects CollectPlacedObjects(RunScope scope, ModTrustTable trust) =>
        scope.Environment.Log.Timed(
            () => PlacedObjectCollector.Collect(scope.Plugin, trust, scope.Settings.Read),
            scope.Report.BuildStepSections);

    private static IRunCaches AddObjectCaches(RunScope scope, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => scope.LoadOrderCaches.AddObjectCaches(
                collected,
                RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors),
                scope.Settings.Execution),
            (_, elapsed) => scope.Report.BuildIndexSections(collected, elapsed));

    private static WriteSummary WritePatch(RunScope scope, StepResults decided) =>
        scope.Environment.Log.Timed(
            () => PatchWriter.Write(decided.Final, decided.Collected.Targets, scope.Plugin, scope.Environment.PatchWriting),
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

    /// <summary>
    /// Selects the objects that can cause removals and finds the objects to keep, then decides the fits of clashing objects
    /// and the removal rounds in order (changed terrain, too close, also remove, leftover invisible objects) and the moves of
    /// the kept markers.
    /// </summary>
    /// <remarks>
    /// The runner holds no error boundary: each step catches its own errors inside the step. Applying a step's
    /// proposals to the shared decisions happens here, inside no step's boundary, so a bug there ends the run. The
    /// apply calls run inside the timed sections, so the printed times include them.
    /// </remarks>
    private static StepResults Decide(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var selected = RunSelection(scope, runCaches, collected);
        var kept = RunKeepCheck(scope, runCaches, collected);
        var started = RemovalDecisions.Start(kept.ObjectsToKeep, collected.Targets.Length);
        var fitted = RunFit(scope, runCaches, trust, collected, selected, kept.ObjectsToKeep, started);
        var changedTerrain = RunChangedTerrain(scope, runCaches, trust, collected, fitted.Decisions);
        var tooClose = RunTooClose(scope, runCaches, collected, selected, changedTerrain.Decisions);
        var alsoRemove = RunAlsoRemove(scope, runCaches, collected, kept.ObjectsToKeep, tooClose.Decisions);
        var visibleTargets = ClashingObjectFitter.IndexAsTheyStand(collected.Targets, alsoRemove.Decisions, runCaches);
        var leftBehind = RunLeftBehind(scope, runCaches, visibleTargets, collected, selected.ObjectsThatCanCauseRemovals, alsoRemove.Decisions);
        var markerMoves = RunMarkerMoves(scope, runCaches, visibleTargets, collected, leftBehind);
        var final = leftBehind.Decisions.AddMoves(markerMoves.Moves);
        return new StepResults(
            Collected: collected,
            ObjectsToKeep: kept.ObjectsToKeep,
            RemovalRisks: kept.Risks,
            ChangedTerrain: changedTerrain.Result,
            RestingObjects: alsoRemove.Result,
            LeftBehind: leftBehind.Result,
            MarkerMoves: markerMoves,
            Final: final);
    }

    private static SelectedObjects RunSelection(RunScope scope, IRunCaches runCaches, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => RemovalCauseSelector.Select(
                collected,
                runCaches.OtherModObjects,
                runCaches.Shapes,
                scope.Settings.Selection,
                scope.Settings.Execution),
            (selected, elapsed) => scope.Report.BuildStepSections(selected.Replacements, collected, elapsed));

    private static ObjectsToKeepResult RunKeepCheck(RunScope scope, IRunCaches runCaches, CollectedObjects collected)
    {
        var kept = ObjectsToKeepFinder.Find(collected.Targets, collected.Links, runCaches.Bases);
        Print(scope.Environment.Log, scope.Report.BuildStepSections(kept));
        return kept;
    }

    private static FitPhase RunFit(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        SelectedObjects selected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions started) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyFits(scope, runCaches, trust, collected, selected, objectsToKeep, started),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, elapsed));

    /// <remarks>
    /// Only a too-close clash is fitted. An object on changed terrain is removed as it stands, since shrinking or turning it does not
    /// change the ground under it; a shrunk or turned placement must not stand on changed terrain either.
    /// </remarks>
    private static FitPhase FindAndApplyFits(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        SelectedObjects selected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions started)
    {
        var rules = new FitRules(
            Fittable: TooCloseObjects.CreatePlacementRule(
                collected,
                runCaches.VisibleTargets,
                selected.ObjectsThatCanCauseRemovals,
                runCaches.PlacedNpcs,
                selected.Replacements,
                runCaches.Shapes,
                runCaches.Triangles,
                scope.Settings.TooClose),
            Unfittable: ChangedTerrainObjects.CreatePlacementRule(
                collected, trust, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, scope.Settings.ChangedTerrain),
            Resting: RestingObjects.CreateRestingRule(
                collected.Targets,
                scope.Settings.AlsoRemove,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.Terrain,
                runCaches.VisibleObjectsOfAnyPlugin,
                scope.Settings.Execution));
        var result = ClashingObjectFitter.Fit(
            collected.Targets, objectsToKeep, rules, runCaches.Shapes, collected.Order, scope.Settings.Execution, scope.Settings.Fit);
        return new FitPhase(result, started.ApplyFits(result.Fits));
    }

    private static ChangedTerrainPhase RunChangedTerrain(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        RemovalDecisions started) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyChangedTerrainRound(scope, runCaches, trust, collected, started),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static ChangedTerrainPhase FindAndApplyChangedTerrainRound(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        RemovalDecisions started)
    {
        var result = ChangedTerrainObjects.Find(
            collected,
            trust,
            scope.Environment.Plugins,
            scope.Plugin,
            runCaches.Shapes,
            scope.Settings.Execution,
            scope.Settings.ChangedTerrain);
        return new ChangedTerrainPhase(result, started.Apply(RoundKind.ChangedTerrain, result.Proposals));
    }

    private static TooClosePhase RunTooClose(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        SelectedObjects selected,
        RemovalDecisions afterChangedTerrain) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyTooCloseRound(scope, runCaches, collected, selected, afterChangedTerrain),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, runCaches.VisibleTargets, elapsed));

    private static TooClosePhase FindAndApplyTooCloseRound(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        SelectedObjects selected,
        RemovalDecisions afterChangedTerrain)
    {
        var result = TooCloseObjects.Find(
            collected,
            runCaches.VisibleTargets,
            selected.ObjectsThatCanCauseRemovals,
            runCaches.PlacedNpcs,
            selected.Replacements,
            runCaches.Shapes,
            runCaches.Triangles,
            scope.Settings.Execution,
            scope.Settings.TooClose);
        return new TooClosePhase(result, afterChangedTerrain.Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <remarks>Not timed here: the step times its rounds itself, through the run's clock.</remarks>
    private static AlsoRemovePhase RunAlsoRemove(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterTooClose)
    {
        var phase = FindAndApplyAlsoRemoveRounds(scope, runCaches, collected, objectsToKeep, afterTooClose);
        Print(scope.Environment.Log, scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected));
        return phase;
    }

    private static AlsoRemovePhase FindAndApplyAlsoRemoveRounds(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterTooClose)
    {
        var result = RestingObjects.Find(
            new RestingObjectsInput(
                ClashingObjectFitter.PlaceAsTheyStand(collected.Targets, afterTooClose),
                objectsToKeep,
                afterTooClose,
                scope.Settings.AlsoRemove,
                scope.Settings.Execution,
                scope.Environment.TimeSinceStart,
                scope.ReportFolder.Outcome),
            runCaches.Bases,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin);
        return new AlsoRemovePhase(result, afterTooClose.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds));
    }

    private static LeftBehindPhase RunLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        RemovalDecisions afterAlsoRemove) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyLeftBehindRound(scope, runCaches, visibleTargets, collected, objectsThatCanCauseRemovals, afterAlsoRemove),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="visibleTargets">The visible targets as they stand.</param>
    private static LeftBehindPhase FindAndApplyLeftBehindRound(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        RemovalDecisions afterAlsoRemove)
    {
        var result = LeftBehindObjects.Find(
            collected.Targets,
            objectsThatCanCauseRemovals,
            afterAlsoRemove,
            visibleTargets,
            runCaches.Bases,
            runCaches.Shapes,
            collected.Order,
            scope.Settings.Execution,
            scope.Settings.LeftBehind);
        return new LeftBehindPhase(result, afterAlsoRemove.Apply(RoundKind.LeftBehind, result.Proposals));
    }

    /// <param name="visibleTargets">The visible targets as they stand.</param>
    private static MarkerMoves RunMarkerMoves(
        RunScope scope, IRunCaches runCaches, IVisibleTargetObjects visibleTargets, CollectedObjects collected, LeftBehindPhase leftBehind)
    {
        var markerMoves = MarkerMovePlanner.Plan(
            collected.Targets,
            leftBehind.Result,
            leftBehind.Decisions,
            runCaches.VisibleObjectsOfAnyPlugin,
            visibleTargets,
            scope.Plugin,
            runCaches.Navmeshes,
            runCaches.Terrain,
            runCaches.Shapes,
            scope.Settings.Execution,
            scope.Settings.MarkerMoves);
        Print(scope.Environment.Log, scope.Report.BuildStepSections(markerMoves, collected));
        return markerMoves;
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

    /// <param name="Decisions">The decisions with the fit round, when there was a fit.</param>
    private sealed record FitPhase(FitResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the changed-terrain round.</param>
    private sealed record ChangedTerrainPhase(ChangedTerrainResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the too-close round.</param>
    private sealed record TooClosePhase(TooCloseResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the also-remove rounds.</param>
    private sealed record AlsoRemovePhase(RestingObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with every round.</param>
    private sealed record LeftBehindPhase(LeftBehindResult Result, RemovalDecisions Decisions);
}
