using System.Collections.Immutable;
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
using AddedObjectRemover.Steps.IdentifyTheMods;
using AddedObjectRemover.Steps.MoveKeptMarkers;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
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
/// Runs one patch run: creates the run's caches, runs the steps in order and hands each step's result to the log
/// and reports as the step ends, printing the sections it returns; then writes the report files.
/// All decisions are made before the patch is written.
/// </summary>
public static class StepRunner
{
    public static RunResult PatchLoadOrder(RunRequest request, RunEnvironment environment)
    {
        var unexpectedErrors = new UnexpectedErrors();
        var log = environment.Log;
        Print(log, LogAndReports.BuildStartSections(request));
        if (request is not RunRequest.Ready ready) return new RunResult.Stopped(((RunRequest.Stop)request).Reason);

        var settings = ready.Settings;
        var reportFolder = environment.ReportOutput.Prepare(settings.Reports);
        Print(log, LogAndReports.BuildReportFolderSections(reportFolder));
        var plugin = environment.PluginRecords.Open();
        var caches = LoadOrderCaches.Create(
            environment.MeshFiles,
            unexpectedErrors,
            plugin,
            environment.Bases,
            TooCloseObjects.NpcBodyRules,
            TooCloseObjects.PlacedNpcRules);
        var report = new LogAndReports(settings, reportFolder, caches, unexpectedErrors);
        return RunSteps(new RunScope(settings, environment, plugin, caches, report, reportFolder), unexpectedErrors);
    }

    private static RunResult RunSteps(RunScope scope, IUnexpectedErrors unexpectedErrors)
    {
        var (settings, environment, plugin, loadOrder, report, _) = scope;
        var log = environment.Log;
        loadOrder.PrepareMeshReading();
        Print(log, report.BuildArchiveSections());
        var mods = ModIdentifier.Identify(environment.Plugins, settings.ModIdentification);
        Print(log, report.BuildStepSections(mods));
        var world = log.Timed(() => PlacedObjectCollector.Collect(plugin, mods, settings.Read), report.BuildStepSections);
        if (world.Stop is { } stop) return StopRun(stop, log);

        var caches = log.Timed(
            () => loadOrder.AddObjectCaches(world, RemovalCauseSelector.MeasurementRules(unexpectedErrors), settings.Execution),
            (_, elapsed) => report.BuildIndexSections(world, elapsed));
        var decided = Decide(scope, caches, world);
        var written = log.Timed(() => PatchWriter.Write(decided.Final, world.Targets, plugin), report.BuildStepSections);
        var ended = report.BuildEndOfRun(decided, written);
        Print(log, ended.Sections);
        var files = environment.ReportOutput.Write(settings.Reports, ended.Tables);
        Print(log, report.BuildFilesWrittenSections(ended, files, environment.TimeSinceStart()));
        return new RunResult.Done(ended.Outcome);
    }

    private static void Print(ILogSink log, ImmutableArray<LogSection> sections)
    {
        foreach (var section in sections) log.Print(section);
    }

    private static RunResult.Stopped StopRun(StopReason reason, ILogSink log)
    {
        Print(log, LogAndReports.BuildStopSections(reason));
        return new RunResult.Stopped(reason);
    }

    /// <summary>Every decision of the run, from the setup through the marker moves.</summary>
    private static StepResults Decide(RunScope scope, IRunCaches caches, CollectedObjects world)
    {
        var (settings, environment, _, _, report, _) = scope;
        var log = environment.Log;
        var selected = log.Timed(
            () => RemovalCauseSelector.Select(world, caches.OtherModObjects, caches.Shapes, settings.Selection, settings.Execution),
            (found, elapsed) => report.BuildStepSections(found.Replacements, world, elapsed));
        var replacements = selected.Replacements;
        var kept = ObjectsToKeepFinder.Find(world.Targets, world.Links);
        var protection = kept.ObjectsToKeep;
        Print(log, report.BuildStepSections(kept));
        var otherModObjects = selected.ObjectsThatCanCauseRemovals;

        var tooClose = FindTooCloseObjects(scope, caches, world, otherModObjects, replacements, protection);
        var restingObjects = RemoveRestingObjects(scope, caches, world, protection, tooClose.Decisions);
        var leftBehind = DecideLeftBehind(scope, caches, world, otherModObjects, restingObjects.Decisions);
        var markerMoves = MoveKeptMarkers(scope, caches, world, leftBehind);
        return new StepResults(
            world, protection, restingObjects.Result, leftBehind.Result, markerMoves, leftBehind.Final.AddMoves(markerMoves.Moves));
    }

    private static TooClosePhase FindTooCloseObjects(
        RunScope scope, IRunCaches caches, CollectedObjects world, IObjectsThatCanCauseRemovals otherModObjects, Replacements replacements, IObjectsToKeep protection)
    {
        var (_, environment, _, _, report, _) = scope;
        return environment.Log.Timed(
            () => DecideTooClose(scope, caches, world, otherModObjects, replacements, protection),
            (decided, elapsed) => report.BuildStepSections(decided.Result, decided.Decisions, world, caches.VisibleTargets, elapsed));
    }

    private static TooClosePhase DecideTooClose(
        RunScope scope, IRunCaches caches, CollectedObjects world, IObjectsThatCanCauseRemovals otherModObjects, Replacements replacements, IObjectsToKeep protection)
    {
        var settings = scope.Settings;
        var result = TooCloseObjects.Find(
            world,
            caches.VisibleTargets,
            otherModObjects,
            caches.PlacedNpcs,
            replacements,
            caches.Shapes,
            caches.Triangles,
            settings.Execution,
            settings.TooClose);
        return new TooClosePhase(result, RemovalDecisions.Start(protection, world.Targets.Length).Apply(RoundKind.TooClose, result.Proposals));
    }

    /// <param name="afterTooClose">The decisions whose last round is the too-close round.</param>
    private static RestingObjectsPhase RemoveRestingObjects(
        RunScope scope, IRunCaches caches, CollectedObjects world, IObjectsToKeep protection, RemovalDecisions afterTooClose)
    {
        var (settings, environment, _, _, report, reportFolder) = scope;
        var result = RestingObjects.Find(
            new RestingObjectsInput(
                world.Targets, protection, afterTooClose, settings.AlsoRemove, settings.Execution, environment.TimeSinceStart, reportFolder.Outcome),
            caches.Bases,
            caches.Shapes,
            caches.Triangles,
            caches.Terrain,
            caches.VisibleObjectsOfAnyPlugin);
        var decisions = afterTooClose.ApplyRounds(RoundKind.AlsoRemove, result.ProposedRounds);
        Print(environment.Log, report.BuildStepSections(result, decisions, world));
        return new RestingObjectsPhase(result, decisions);
    }

    /// <param name="afterRestingObjects">The decisions after the also-remove rounds.</param>
    private static LeftBehindPhase DecideLeftBehind(
        RunScope scope, IRunCaches caches, CollectedObjects world, IObjectsThatCanCauseRemovals otherModObjects, RemovalDecisions afterRestingObjects)
    {
        var report = scope.Report;
        return scope.Environment.Log.Timed(
            () => ApplyLeftBehind(afterRestingObjects, FindLeftBehind(scope, caches, world, otherModObjects, afterRestingObjects)),
            (found, elapsed) => report.BuildStepSections(found.Result, found.Final, world, elapsed));
    }

    private static LeftBehindResult FindLeftBehind(
        RunScope scope, IRunCaches caches, CollectedObjects world, IObjectsThatCanCauseRemovals otherModObjects, RemovalDecisions afterRestingObjects)
    {
        var settings = scope.Settings;
        return LeftBehindObjects.Find(
            world.Targets, otherModObjects, afterRestingObjects, caches.VisibleTargets, caches.Bases, caches.Shapes, world.Order, settings.Execution, settings.LeftBehind);
    }

    /// <remarks>Adding the round to the shared decisions is outside the step's own error boundary; it must not fail halfway, so a bug there ends the run.</remarks>
    private static LeftBehindPhase ApplyLeftBehind(RemovalDecisions afterRestingObjects, LeftBehindResult found) =>
        new(found, afterRestingObjects.Apply(RoundKind.LeftBehind, [.. found.Proposals]));

    private static MarkerMoves MoveKeptMarkers(RunScope scope, IRunCaches caches, CollectedObjects world, LeftBehindPhase leftBehind)
    {
        var (settings, environment, plugin, _, report, _) = scope;
        var markerMoves = MarkerMovePlanner.Plan(
            world.Targets,
            leftBehind.Result,
            leftBehind.Final,
            caches.VisibleObjectsOfAnyPlugin,
            caches.VisibleTargets,
            plugin,
            caches.Navmeshes,
            caches.Terrain,
            caches.Shapes,
            settings.Execution,
            settings.MarkerMoves);
        Print(environment.Log, report.BuildStepSections(markerMoves, world));
        return markerMoves;
    }

    /// <summary>What every phase function reads besides its own inputs.</summary>
    /// <param name="ReportFolder">What the report writer prepared, which says whether the report files are written.</param>
    private sealed record RunScope(
        RunSettings Settings, RunEnvironment Environment, IPluginRecords Plugin, LoadOrderCaches LoadOrder, LogAndReports Report, ReportFolderPrepared ReportFolder);

    /// <param name="Decisions">The decisions with the too-close round.</param>
    private sealed record TooClosePhase(TooCloseResult Result, RemovalDecisions Decisions);

    /// <param name="Decisions">The decisions with the also-remove rounds.</param>
    private sealed record RestingObjectsPhase(RestingObjectsResult Result, RemovalDecisions Decisions);

    /// <param name="Final">The decisions with every round.</param>
    private sealed record LeftBehindPhase(LeftBehindResult Result, RemovalDecisions Final);
}
