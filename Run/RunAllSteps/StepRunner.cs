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
using AddedObjectRemover.Steps.JudgeChangedObjects;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
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
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
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
            WeightedSurfaceSamples.SamplesPerMesh);
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
        var scanned = PlacedObjectCollector.ScanPlacedRecords(scope.Plugin, trust, trust.AcceptedWorld, scope.Settings.Read);
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
            () => PatchWriter.Write(decided.Final, decided.Collected.Targets, decided.OtherModsObjects, decided.RestoreWhereRemoved, scope.Plugin, scope.Environment.PatchWriting),
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
    /// decides the fits of clashing objects and the removal rounds in order (changed terrain, too close, also remove, visible objects left behind
    /// with the also-remove follow-up of each of its rounds, other mods' objects left without support with the target objects that go
    /// with them, leftover invisible objects), then the moves of the kept markers, and last what the cleaned mod changed that goes
    /// back where it was removed.
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
        var judged = JudgeChangedObjects(scope, runCaches, trust, collected);
        var started = RemovalDecisions.Start(kept.ObjectsToKeep, judged.Result.WhenRemoved);
        var groups = scope.Settings.CollisionGroupsOf(collected.Targets);
        var fitted = RunFit(scope, runCaches, trust, collected, selected, groups, heldUp, kept.ObjectsToKeep, started);
        var changedTerrain = RunChangedTerrain(scope, runCaches, trust, collected, groups, fitted.Decisions);
        var tooClose = RunTooClose(scope, runCaches, collected, selected, groups, changedTerrain.Decisions);
        var alsoRemove = RunAlsoRemove(scope, runCaches, collected, tooClose.Decisions);
        var visibleLeftBehind = RunVisibleLeftBehind(scope, runCaches, collected, alsoRemove.Decisions);
        var otherMods = RunOtherModsObjects(scope, runCaches, trust, collected, fitted.Decisions, visibleLeftBehind.Decisions);
        var otherModsObjectsTakenAway = otherMods.Result.TakenAway();
        var visibleTargetsAsTheRoundsMeasureThem = runCaches.IndexVisibleTargetsAsTheRoundsMeasureThem(collected.Targets, otherMods.Decisions);
        var leftBehind = RunLeftBehind(
            scope,
            runCaches,
            visibleTargetsAsTheRoundsMeasureThem,
            collected,
            selected.ObjectsThatCanCauseRemovals,
            otherModsObjectsTakenAway,
            otherMods.Decisions);
        var markerMoves = RunMarkerMoves(
            scope,
            runCaches,
            runCaches.IndexVisibleTargetsAsTheyStand(collected.Targets, leftBehind.Decisions),
            collected,
            otherModsObjectsTakenAway,
            leftBehind);
        var final = leftBehind.Decisions.AddMoves(markerMoves.Moves);
        var whereRemoved = RunRestoreWhereRemoved(scope, runCaches, trust, visibleTargetsAsTheRoundsMeasureThem, collected, otherMods.Result, final);
        return new StepResults(
            Collected: collected,
            ObjectsToKeep: kept.ObjectsToKeep,
            RemovalRisks: kept.Risks,
            ChangedObjects: judged.Result,
            OldPlaceWorld: judged.OldPlaceWorld,
            ChangedTerrain: changedTerrain.Result,
            RestingObjects: alsoRemove.Result,
            VisibleLeftBehind: visibleLeftBehind.Result,
            LeftBehind: leftBehind.Result,
            MarkerMoves: markerMoves,
            OtherModsObjects: otherMods.Result,
            Final: final,
            RestoreWhereRemoved: whereRemoved);
    }

    private static HeldUpTargets RunHeldUpTargets(RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => OtherModsObjects.FindHeldUpTargets(
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
        RemovalDecisions afterFits,
        RemovalDecisions afterVisibleLeftBehind) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyOtherModsObjectsRounds(scope, runCaches, trust, collected, afterFits, afterVisibleLeftBehind),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="afterFits">The decisions with every fit: the rounds measure an object shrunk or turned and removed later at its new placement.</param>
    private static OtherModsObjectsPhase FindAndApplyOtherModsObjectsRounds(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        RemovalDecisions afterFits,
        RemovalDecisions afterVisibleLeftBehind)
    {
        var targetsAsMeasured = afterFits.TargetsAsTheRoundsMeasureThem(collected.Targets);
        var result = OtherModsObjects.SettleLeftWithoutSupport(
            collected.Targets,
            targetsAsMeasured,
            afterVisibleLeftBehind,
            runCaches.VisibleTargets,
            runCaches.IndexVisibleTargetsAsTheRoundsMeasureThem(collected.Targets, afterFits),
            placed => runCaches.IndexVisibleTargets(placed),
            trust,
            scope.Environment.Plugins,
            scope.Settings.OtherModsObjects,
            scope.Settings.AlsoRemove,
            decided => RestingObjects.CreateRoundsAfter(
                decided,
                targetsAsMeasured,
                runCaches.VisibleObjectsOfAnyPlugin(),
                scope.Settings.AlsoRemove,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.SurfaceSamples,
                runCaches.Terrain,
                scope.Settings.Execution),
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            scope.Settings.Execution);
        return new OtherModsObjectsPhase(result, afterVisibleLeftBehind.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds));
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

    private static ChangedObjectsPhase JudgeChangedObjects(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected) =>
        scope.Environment.Log.Timed(
            () => PrepareChangedObjects(scope, runCaches, trust, collected),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, collected, phase.OldPlaceWorld, elapsed));

    private static ChangedObjectsPhase PrepareChangedObjects(
        RunScope scope, IRunCaches runCaches, ModTrustTable trust, CollectedObjects collected)
    {
        var world = new ComputedOnce<CollectedObjects>(() => ChangedObjectJudging.CreateOldPlaceWorld(collected, trust.CleanedMod, scope.Settings.Read));
        var result = ChangedObjectJudging.Prepare(
            collected, CreateOldPlaceRules(scope, runCaches, trust, collected, world), scope.Settings.ChangedObjects, scope.Settings.Execution);
        return new ChangedObjectsPhase(result, world.ValueIfComputed);
    }

    /// <remarks>
    /// The objects of the old place, their caches and the terrain check are built when the first earlier version is checked, so a run
    /// whose changed objects need no check builds none of them.
    /// </remarks>
    private static OldPlaceRules CreateOldPlaceRules(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        ComputedOnce<CollectedObjects> world)
    {
        var caches = new ComputedOnce<IRunCaches>(
            () => scope.LoadOrderCaches.AddObjectCaches(world.Value, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors), scope.Settings.Execution));
        return new OldPlaceRules(
            target => TooCloseObjects.CreatePlacementRule(
                collected.Targets,
                runCaches.VisibleTargets,
                RemovalCauseSelector.SelectForOldPlace(
                    caches.Value.OtherModObjects,
                    world.Value.OtherModObjects.Length,
                    [ChangedObjectJudging.OldPlaceIdOf(target)],
                    scope.Settings.ChangedObjectCollision.TooClose.Npcs),
                caches.Value.PlacedNpcs,
                runCaches.Shapes,
                runCaches.Triangles,
                scope.Settings.ChangedObjectCollision.TooClose),
            ChangedTerrainObjects.CreatePlacementRule(
                trust.AcceptedWorldWithoutCleanedMod,
                scope.Environment.Plugins,
                scope.Plugin,
                runCaches.Shapes,
                scope.Settings.ChangedObjectCollision.ChangedTerrain));
    }

    private static FitPhase RunFit(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        SelectedObjects selected,
        ImmutableArray<CollisionGroup> groups,
        HeldUpTargets heldUp,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions started) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyFits(scope, runCaches, trust, collected, selected, groups, heldUp, objectsToKeep, started),
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
        ImmutableArray<CollisionGroup> groups,
        HeldUpTargets heldUp,
        IObjectsToKeep objectsToKeep,
        RemovalDecisions started)
    {
        ImmutableArray<FitGroup> fitGroups =
        [
            .. groups.Select(group => new FitGroup(
                group.Fit,
                Fittable: TooCloseObjects.CreatePlacementRule(
                    collected.Targets,
                    runCaches.VisibleTargets,
                    selected,
                    runCaches.PlacedNpcs,
                    runCaches.Shapes,
                    runCaches.Triangles,
                    group.TooClose.Settings),
                Unfittable: ChangedTerrainObjects.CreatePlacementRule(
                    trust.AcceptedWorld, scope.Environment.Plugins, scope.Plugin, runCaches.Shapes, group.ChangedTerrain.Settings))),
        ];
        var targetObjectsResting = RestingObjects.CreateRestingRule(
            collected.Targets,
            scope.Settings.AlsoRemove,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin,
            OtherModsObjects.CreateDropTest(
                collected.Targets,
                runCaches.VisibleTargets,
                trust,
                scope.Environment.Plugins,
                scope.Settings.AlsoRemove,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.SurfaceSamples,
                runCaches.Terrain,
                runCaches.VisibleObjectsOfAnyPlugin,
                scope.Settings.Execution),
            scope.Settings.Execution);
        var resting = OtherModsObjects.CreateRestingRule(
            targetObjectsResting,
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
            heldUp,
            scope.Settings.Execution);
        var result = ClashingObjectFitter.Fit(
            collected.Targets, objectsToKeep, fitGroups, resting, runCaches.Shapes, collected.Order, scope.Settings.Execution);
        return new FitPhase(result, started.ApplyFits(result.Fits));
    }

    private static ChangedTerrainPhase RunChangedTerrain(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        RemovalDecisions started) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyChangedTerrainRound(scope, runCaches, trust, collected, groups, started),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static ChangedTerrainPhase FindAndApplyChangedTerrainRound(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        CollectedObjects collected,
        ImmutableArray<CollisionGroup> groups,
        RemovalDecisions started)
    {
        var result = ChangedTerrainObjects.Find(
            collected,
            trust.AcceptedWorld,
            scope.Environment.Plugins,
            scope.Plugin,
            runCaches.Shapes,
            scope.Settings.Execution,
            groups.Select(group => group.ChangedTerrain));
        return new ChangedTerrainPhase(result, started.Apply(RoundKind.ChangedTerrain, result.Proposals));
    }

    private static TooClosePhase RunTooClose(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        SelectedObjects selected,
        ImmutableArray<CollisionGroup> groups,
        RemovalDecisions afterChangedTerrain) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyTooCloseRound(scope, runCaches, collected, selected, groups, afterChangedTerrain),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, runCaches.VisibleTargets, elapsed));

    private static TooClosePhase FindAndApplyTooCloseRound(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        SelectedObjects selected,
        ImmutableArray<CollisionGroup> groups,
        RemovalDecisions afterChangedTerrain)
    {
        var result = TooCloseObjects.Find(
            collected,
            runCaches.VisibleTargets,
            selected,
            runCaches.PlacedNpcs,
            runCaches.Shapes,
            runCaches.Triangles,
            scope.Settings.Execution,
            groups.Select(group => group.TooClose));
        return new TooClosePhase(result, afterChangedTerrain.Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <remarks>The step also times its rounds alone, through the run's clock.</remarks>
    private static AlsoRemovePhase RunAlsoRemove(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        RemovalDecisions afterTooClose) =>
        scope.Environment.Log.Timed(
            () => FindAndApplyAlsoRemoveRounds(scope, runCaches, collected, afterTooClose),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    /// <param name="earlierRounds">The decisions with the rounds of the steps before, whose removals the rounds follow up.</param>
    private static AlsoRemovePhase FindAndApplyAlsoRemoveRounds(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        RemovalDecisions earlierRounds)
    {
        var result = RestingObjects.Find(
            new RestingObjectsInput(
                earlierRounds.TargetsAsTheRoundsMeasureThem(collected.Targets),
                earlierRounds,
                earlierRounds.Rounds,
                scope.Settings.AlsoRemove,
                scope.Settings.Execution,
                scope.Environment.TimeSinceStart,
                scope.ReportFolder.Outcome),
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
            () => FindAndApplyVisibleLeftBehindRounds(scope, runCaches, collected, afterAlsoRemove),
            (phase, elapsed) => scope.Report.BuildStepSections(phase.Result, phase.Decisions, collected, elapsed));

    private static VisibleLeftBehindPhase FindAndApplyVisibleLeftBehindRounds(
        RunScope scope,
        IRunCaches runCaches,
        CollectedObjects collected,
        RemovalDecisions afterAlsoRemove)
    {
        var result = VisibleLeftBehindObjects.Find(
            collected.Targets,
            afterAlsoRemove,
            runCaches.IndexVisibleTargetsAsTheRoundsMeasureThem(collected.Targets, afterAlsoRemove),
            runCaches.Bases,
            runCaches.Shapes,
            CreateAlsoRemoveFollowUp(scope, runCaches, afterAlsoRemove.TargetsAsTheRoundsMeasureThem(collected.Targets)),
            scope.Environment.TimeSinceStart,
            collected.Order,
            scope.Settings.Execution,
            scope.Settings.VisibleLeftBehind);
        return new VisibleLeftBehindPhase(result, afterAlsoRemove.ApplyRounds(result.ProposedRounds));
    }

    /// <param name="targetsAsMeasured">Where the rounds measure the targets when the follow-ups start.</param>
    private static IAlsoRemoveFollowUp CreateAlsoRemoveFollowUp(RunScope scope, IRunCaches runCaches, ImmutableArray<TargetObject> targetsAsMeasured) =>
        RestingObjects.CreateFollowUp(
            targetsAsMeasured,
            scope.Settings.AlsoRemove,
            scope.Settings.Execution,
            scope.Environment.TimeSinceStart,
            runCaches.Shapes,
            runCaches.Triangles,
            runCaches.SurfaceSamples,
            runCaches.Terrain,
            runCaches.VisibleObjectsOfAnyPlugin);

    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
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

    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
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

    /// <param name="visibleTargets">The visible targets where the rounds measure them.</param>
    /// <param name="final">The decisions with every round and every move.</param>
    /// <remarks>
    /// The object caches of the world its guards judge in are built only when the step asks, so a run with no placed record to guard
    /// builds none; the step gives them only the objects its guards can reach.
    /// </remarks>
    private static RestoreWhereRemovedResult RunRestoreWhereRemoved(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final) =>
        scope.Environment.Log.Timed(
            () => PlanWhereRemoved(scope, runCaches, trust, visibleTargets, collected, otherModsObjects, final),
            (result, elapsed) => scope.Report.BuildStepSections(result, collected, elapsed));

    private static RestoreWhereRemovedResult PlanWhereRemoved(
        RunScope scope,
        IRunCaches runCaches,
        ModTrustTable trust,
        IVisibleTargetObjects visibleTargets,
        CollectedObjects collected,
        OtherModsObjectsResult otherModsObjects,
        IRemovalDecisions final) =>
        WhereRemovedRestorer.Plan(
            collected,
            final,
            otherModsObjects,
            visibleTargets,
            world => scope.LoadOrderCaches.AddObjectCaches(
                world, RemovalCauseSelector.MeasurementRules(scope.UnexpectedErrors, RestoreWhereRemovedParts.ObjectThatStays), scope.Settings.Execution),
            (world, caches, excluded, settings) => TooCloseObjects.CreatePlacementRule(
                world.Targets,
                caches.VisibleTargets,
                RemovalCauseSelector.SelectForOldPlace(caches.OtherModObjects, world.OtherModObjects.Length, excluded, settings.Npcs),
                caches.PlacedNpcs,
                runCaches.Shapes,
                runCaches.Triangles,
                settings),
            (holders, holderIndex, movesAway, staying, copiesOfMoving) => OtherModsObjects.FindHoldersOfObjectsThatStay(
                holders,
                holderIndex,
                movesAway,
                staying,
                copiesOfMoving,
                trust,
                scope.Environment.Plugins,
                scope.Settings.OtherModsObjects,
                runCaches.Shapes,
                runCaches.Triangles,
                runCaches.SurfaceSamples,
                runCaches.Terrain,
                scope.Settings.Execution),
            scope.Settings.OtherModsObjects,
            runCaches.Shapes,
            runCaches.Navmeshes,
            scope.Plugin.FindLandWorldspace,
            cell => new LandHeights(scope.Plugin.ReadTerrainBelow(trust.CleanedMod, cell), scope.Plugin.ReadTerrain(cell)),
            orders => scope.Environment.PatchWriting.FindNavmeshFaultIfWritten(scope.Plugin.ReadWritableRecords(), orders),
            scope.Environment.Plugins,
            trust.CleanedMod,
            scope.Settings.Execution,
            scope.Settings.RestoreWhereRemoved);

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

    /// <param name="OldPlaceWorld">The objects the old-place checks were made among; null when no check was made.</param>
    private sealed record ChangedObjectsPhase(ChangedObjectJudgingResult Result, CollectedObjects? OldPlaceWorld);

    /// <param name="Decisions">The decisions with the fit round, when there was a fit.</param>
    private sealed record FitPhase(FitResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the changed-terrain round.</param>
    private sealed record ChangedTerrainPhase(ChangedTerrainResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the too-close round.</param>
    private sealed record TooClosePhase(TooCloseResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the also-remove rounds.</param>
    private sealed record AlsoRemovePhase(RestingObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the visible left-behind rounds and their follow-up rounds.</param>
    private sealed record VisibleLeftBehindPhase(VisibleLeftBehindResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the rounds for the target objects that went with other mods' objects taken away.</param>
    private sealed record OtherModsObjectsPhase(OtherModsObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with every round.</param>
    private sealed record LeftBehindPhase(LeftBehindResult Result, RemovalDecisions Decisions);
}
