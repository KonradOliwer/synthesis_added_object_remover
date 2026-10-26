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
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>
/// Runs the patch runs of a batch one after the other. Each run creates its own caches, runs the steps in order and
/// hands each step's result to the log and reports as the step ends, printing the sections it returns; then it
/// writes the report files. All decisions of a run are made before its patch is written. Nothing passes between runs: a
/// run decides from the world, which includes what earlier runs wrote into the patch.
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
            () => ModTrustClassifier.Classify(scope.Environment.Plugins, scope.Settings.ModTrust),
            (trust, elapsed) => scope.Report.BuildStepSections(trust, scope.Environment.Plugins, elapsed));

    /// <remarks>Timed through the run's clock, since the log times one piece of work and the two parts print one section.</remarks>
    private static CollectedObjects CollectPlacedObjects(RunScope scope, ModTrustTable trust)
    {
        var clock = scope.Environment.TimeSinceStart;
        var start = clock();
        var scanned = PlacedObjectCollector.ScanPlacedRecords(scope.Plugin, trust, scope.Settings.Read);
        var scanEnd = clock();
        var collected = PlacedObjectCollector.ReadNonPlacedLinks(scanned, scope.Plugin);
        Print(scope.Environment.Log, scope.Report.BuildStepSections(collected, scanEnd - start, clock() - scanEnd));
        return collected;
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
            () => PatchWriter.Write(decided.Final, decided.Collected.Targets, decided.OtherModsObjects, scope.Plugin, scope.Environment.PatchWriting),
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
    /// Selects the objects that can cause removals and finds the objects to keep, other mods' objects rest on included, then
    /// decides the fits of clashing objects and the removal rounds in order (changed terrain, too close, also remove, visible objects left behind,
    /// also remove again for the visible objects left behind, other mods' objects left without support with the target objects that go
    /// with them, leftover invisible objects), then the moves of the kept markers.
    /// </summary>
    /// <remarks>
    /// The runner holds no error boundary: each step catches its own errors inside the step. Applying a step's
    /// proposals to the shared decisions happens here, inside no step's boundary, so a bug there ends the run. The
    /// apply calls run inside the timed sections, so the printed times include them.
    /// </remarks>
    private static StepResults Decide(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var selected = RunSelection(scope, runCaches, collected);
        var heldUp = RunHeldUpTargets(scope, runCaches, trust, collected);
        var kept = RunKeepCheck(scope, runCaches, collected, heldUp);
        var started = RemovalDecisions.Start(kept.ObjectsToKeep, collected.Targets.Length);
        var fitted = RunFit(scope, runCaches, trust, collected, selected, heldUp, kept.ObjectsToKeep, started);
        var changedTerrain = RunChangedTerrain(scope, runCaches, trust, collected, fitted.Decisions);
        var tooClose = RunTooClose(scope, runCaches, collected, selected, changedTerrain.Decisions);
        var alsoRemove = RunAlsoRemove(scope, runCaches, collected, kept.ObjectsToKeep, tooClose.Decisions);
        var visibleLeftBehind = RunVisibleLeftBehind(scope, runCaches, collected, alsoRemove.Decisions);
        var alsoRemoveAfterVisibleLeftBehind = RunAlsoRemoveAfterVisibleLeftBehind(
            scope, runCaches, collected, kept.ObjectsToKeep, visibleLeftBehind.Decisions, alsoRemove.Decisions);
        var otherMods = RunOtherModsObjects(scope, runCaches, trust, collected, kept.ObjectsToKeep, fitted.Decisions, alsoRemoveAfterVisibleLeftBehind.Decisions);
        var visibleTargets = ClashingObjectFitter.IndexAsTheyStand(collected.Targets, otherMods.Decisions, runCaches);
        var otherModsObjectsTakenAway = otherMods.Result.TakenAway();
        var leftBehind = RunLeftBehind(
            scope, runCaches, visibleTargets, collected, selected.ObjectsThatCanCauseRemovals, otherModsObjectsTakenAway, otherMods.Decisions);
        var markerMoves = RunMarkerMoves(scope, runCaches, visibleTargets, collected, otherModsObjectsTakenAway, leftBehind);
        var final = leftBehind.Decisions.AddMoves(markerMoves.Moves);
        return new StepResults(
            Collected: collected,
            ObjectsToKeep: kept.ObjectsToKeep,
            RemovalRisks: kept.Risks,
            ChangedTerrain: changedTerrain.Result,
            RestingObjects: alsoRemove.Result,
            VisibleLeftBehind: visibleLeftBehind.Result,
            RestingObjectsAfterVisibleLeftBehind: alsoRemoveAfterVisibleLeftBehind.Result,
            LeftBehind: leftBehind.Result,
            MarkerMoves: markerMoves,
            OtherModsObjects: otherMods.Result,
            Final: final);
    }

    private static HeldUpTargets RunHeldUpTargets(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => RestingObjects.FindHeldUpTargets(
                collected.Targets,
                runCaches.VisibleTargets,
                trust,
                scope.Environment.Plugins,
                scope.Settings.OtherModsObjects,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.SurfaceSamples,
                runCaches.Terrain,
                runCaches.VisibleObjectsOfAnyPlugin,
                scope.Settings.Execution),
            (heldUp, elapsed) => scope.Report.BuildStepSections(heldUp, elapsed));

    /// <param name="afterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    private static OtherModsObjectsPhase RunOtherModsObjects(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterFits,
        RemovalDecisions afterAlsoRemove) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyOtherModsObjectsRounds(scope, runCaches, trust, collected, objectsToKeep, afterFits, afterAlsoRemove),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="afterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    private static OtherModsObjectsPhase FindAndApplyOtherModsObjectsRounds(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterFits,
        RemovalDecisions afterAlsoRemove)
    {
        var result = RestingObjects.SettleOtherModsObjects(
            collected.Targets,
            ClashingObjectFitter.PlaceAsTheyStand(collected.Targets, afterFits),
            afterAlsoRemove,
            objectsToKeep,
            runCaches.VisibleTargets,
            ClashingObjectFitter.IndexAsTheyStand(collected.Targets, afterFits, runCaches),
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            scope.Settings.AlsoRemove,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            scope.Settings.Execution);
        return new OtherModsObjectsPhase(result, afterAlsoRemove.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds));
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

    private static ObjectsToKeepResult RunKeepCheck(RunScope scope, IRunCaches runCaches, CollectedObjects collected, HeldUpTargets heldUp) =>
        scope.Environment.Log.Timed(
            () => ObjectsToKeepFinder.Find(collected.Targets, collected.Links, runCaches.Bases, heldUp.Targets),
            scope.Report.BuildStepSections);

    private static FitPhase RunFit(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        SelectedObjects selected,
        HeldUpTargets heldUp,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions started) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyFits(scope, runCaches, trust, collected, selected, heldUp, objectsToKeep, started),
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
        HeldUpTargets heldUp,
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
                runCaches.VisibleTargets,
                scope.Settings.AlsoRemove,
                trust,
                scope.Environment.Plugins,
                scope.Settings.OtherModsObjects,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.SurfaceSamples,
                runCaches.Terrain,
                runCaches.VisibleObjectsOfAnyPlugin,
                heldUp,
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

    /// <remarks>The step also times its rounds alone, through the run's clock.</remarks>
    private static AlsoRemovePhase RunAlsoRemove(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterTooClose) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyAlsoRemoveRounds(scope, runCaches, collected, objectsToKeep, afterTooClose, afterTooClose.Rounds, scope.ReportFolder.Outcome),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <remarks>
    /// Seeded by the visible left-behind round alone: what the earlier also-remove rounds started from was followed up already. The
    /// report files of the first also-remove step cover its rounds only.
    /// </remarks>
    private static AlsoRemovePhase RunAlsoRemoveAfterVisibleLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions afterVisibleLeftBehind,
        RemovalDecisions afterAlsoRemove) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyAlsoRemoveRounds(
                scope, runCaches, collected, objectsToKeep, afterVisibleLeftBehind, afterVisibleLeftBehind.RoundsAfter(afterAlsoRemove), StepOutcome.SwitchedOff),
            (phase, elapsed) => scope.Report.BuildFollowUpAfterVisibleLeftBehindSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="earlierRounds">The decisions with the rounds of the steps before.</param>
    /// <param name="seedRounds">The rounds of <paramref name="earlierRounds"/> whose removals the rounds follow up.</param>
    /// <param name="reportFiles">Whether the step computes the details of the report files.</param>
    private static AlsoRemovePhase FindAndApplyAlsoRemoveRounds(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions earlierRounds,
        ImmutableArray<Round> seedRounds,
        StepOutcome reportFiles)
    {
        var result = RestingObjects.Find(
            new RestingObjectsInput(
                ClashingObjectFitter.PlaceAsTheyStand(collected.Targets, earlierRounds),
                objectsToKeep,
                earlierRounds,
                seedRounds,
                scope.Settings.AlsoRemove,
                scope.Settings.Execution,
                scope.Environment.TimeSinceStart,
                reportFiles),
            runCaches.Bases,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin);
        return new AlsoRemovePhase(result, earlierRounds.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds));
    }

    private static VisibleLeftBehindPhase RunVisibleLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        RemovalDecisions afterAlsoRemove) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyVisibleLeftBehindRound(scope, runCaches, collected, afterAlsoRemove),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static VisibleLeftBehindPhase FindAndApplyVisibleLeftBehindRound(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        RemovalDecisions afterAlsoRemove)
    {
        var result = VisibleLeftBehindObjects.Find(
            collected.Targets,
            afterAlsoRemove,
            ClashingObjectFitter.IndexAsTheyStand(collected.Targets, afterAlsoRemove, runCaches),
            runCaches.Bases,
            runCaches.Shapes,
            collected.Order,
            scope.Settings.Execution,
            scope.Settings.VisibleLeftBehind);
        return new VisibleLeftBehindPhase(result, afterAlsoRemove.Apply(RoundKind.VisibleLeftBehind, result.Proposals));
    }

    /// <param name="otherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    private static LeftBehindPhase RunLeftBehind(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        IReadOnlyDictionary<OtherId, OtherObject?> otherModsObjectsTakenAway,
        RemovalDecisions afterOtherModsObjects) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyLeftBehindRound(scope, runCaches, visibleTargets, collected, objectsThatCanCauseRemovals, otherModsObjectsTakenAway, afterOtherModsObjects),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="visibleTargets">The visible targets as they stand.</param>
    /// <param name="otherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    private static LeftBehindPhase FindAndApplyLeftBehindRound(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IObjectsThatCanCauseRemovals objectsThatCanCauseRemovals,
        IReadOnlyDictionary<OtherId, OtherObject?> otherModsObjectsTakenAway,
        RemovalDecisions afterOtherModsObjects)
    {
        var result = LeftBehindObjects.Find(
            collected.Targets,
            objectsThatCanCauseRemovals,
            otherModsObjectsTakenAway.Keys.ToHashSet(),
            afterOtherModsObjects,
            visibleTargets,
            runCaches.Bases,
            runCaches.Shapes,
            collected.Order,
            scope.Settings.Execution,
            scope.Settings.LeftBehind);
        return new LeftBehindPhase(result, afterOtherModsObjects.Apply(RoundKind.LeftBehind, result.Proposals));
    }

    /// <param name="visibleTargets">The visible targets as they stand.</param>
    /// <param name="otherModsObjectsTakenAway">The other mods' objects pulled down (where they stand now) or removed (null), by id.</param>
    private static MarkerMoves RunMarkerMoves(
        RunScope scope,
        IRunCaches runCaches,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        IReadOnlyDictionary<OtherId, OtherObject?> otherModsObjectsTakenAway,
        LeftBehindPhase leftBehind) =>
        scope.Environment.Log.Timed(
            () => MarkerMovePlanner.Plan(
                collected.Targets,
                leftBehind.Result,
                leftBehind.Decisions,
                () => runCaches.VisibleObjectsOfAnyPluginAsTheyStand(otherModsObjectsTakenAway),
                visibleTargets,
                scope.Plugin,
                runCaches.Navmeshes,
                runCaches.Terrain,
                runCaches.Shapes,
                scope.Settings.Execution,
                scope.Settings.MarkerMoves),
            (markerMoves, elapsed) => scope.Report.BuildStepSections(markerMoves, collected, elapsed));

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

    /// <param name="Decisions">The decisions with the rounds of one also-remove pass.</param>
    private sealed record AlsoRemovePhase(RestingObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the visible left-behind round.</param>
    private sealed record VisibleLeftBehindPhase(VisibleLeftBehindResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the rounds for the target objects that went with other mods' objects taken away.</param>
    private sealed record OtherModsObjectsPhase(OtherModsObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with every round.</param>
    private sealed record LeftBehindPhase(LeftBehindResult Result, RemovalDecisions Decisions);
}
