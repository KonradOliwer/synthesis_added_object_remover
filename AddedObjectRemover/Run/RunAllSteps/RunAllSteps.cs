using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep;
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
using CachesOfTheRun = AddedObjectRemover.Run.RunAllSteps.RunCaches.RunCaches;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>
/// Runs one patch run: prints the warnings, creates the run's caches, runs the steps in order and prints each phase's
/// log section right after it, then the closing sections, the report files' lines and the final line.
/// All decisions are made before the patch is written; the report files' evidence is computed after.
/// </summary>
internal static class RunAllSteps
{
    public static RunResult Run(RunRequest request, RunEnvironment environment)
    {
        var log = environment.Log;
        log.Print(LogSections.Warnings(request.Warnings));
        if (request is not RunRequest.Ready ready) return StopRun(((RunRequest.Stop)request).Reason, log);

        var options = ready.Options;
        if (environment.ReportOutput.Prepare(options.Reports) is { } folderWarning) log.Print(LogSections.ReportFolderWarning(folderWarning));

        var plugin = environment.PluginRecords.Open();
        var caches = CachesOfTheRun.CreateForLoadOrder(environment.MeshFiles, plugin, environment.Bases);
        return RunSteps(options, environment, plugin, caches);
    }

    private static RunResult RunSteps(RunSettings options, RunEnvironment environment, IPluginRecords plugin, CachesOfTheRun caches)
    {
        var (mods, log) = (environment.Plugins, environment.Log);
        var context = caches.ReportContext(options.DetailedLog);
        caches.PrepareMeshReading();
        log.Print(LogSections.ArchiveWarnings(caches.TakeArchiveProblems()));
        PrintUnexpectedErrors(caches, log, context);
        var standing = IdentifyTheMods.Decide(mods, options.ModIdentification);
        log.Print(LogSections.Config(options, standing));
        var world = log.Timed(
            () => PlacedObjectCollector.Collect(plugin, standing, options.Read),
            (read, elapsed) => LogSections.Scan(read, options.TargetAsTyped, elapsed, context));
        if (world.Targets.IsEmpty) return StopRun(new StopReason(StopKind.NoTargetObjects, null), log);

        log.Print(LogSections.Threads(options.Execution, context));
        var (decided, restingObjects) = Decide(options, world, plugin, caches, log, context);
        var removals = RemovalList.Removals(decided.Final, decided.World, decided.LeftBehind).ToImmutableArray();
        var written = log.Timed(
            () => PatchWriter.Run(PlanPatch(decided, removals), world.Targets, plugin),
            (summary, elapsed) => LogSections.Write(summary, elapsed, context));

        var explanations = Explain(options, decided, restingObjects, caches);
        log.Print(LogSections.ExplanationProblems(caches.TakeProblems(), context));
        // Only a problem found by reading meshes (the missing strip fields) can still be new here.
        log.Print(LogSections.ArchiveWarnings(caches.TakeArchiveProblems()));
        PrintUnexpectedErrors(caches, log, context);
        var done = new RunResult.Done(ToOutcome(decided, removals, written, caches), explanations);
        PrintClosingSections(done.Outcome, context, log);
        WriteReports(options, done, environment.ReportOutput, caches, context, log);
        PrintUnexpectedErrors(caches, log, context);
        log.Print(LogSections.Done(environment.TimeSinceStart(), context));
        return done;
    }

    private static void PrintUnexpectedErrors(CachesOfTheRun caches, ILogSink log, ReportContext context) =>
        log.Print(LogSections.UnexpectedErrors(caches.TakeUnexpectedErrors(), context));

    /// <summary>Records the unexpected failures of single target objects, which the log groups.</summary>
    private static void RecordTargetFailures(
        CachesOfTheRun caches, CollectedObjects world, IEnumerable<TargetFailure> failures, string part, string consequence)
    {
        foreach (var failure in failures) caches.RecordUnexpected(ObjectErrors.ForTarget(failure, world.Targets[failure.TargetIndex], part, consequence));
    }

    private static RunResult.Stopped StopRun(StopReason reason, ILogSink log)
    {
        log.Print(LogSections.Stop(reason));
        return new RunResult.Stopped(reason);
    }

    /// <summary>The closing sections: what was removed and kept, and what may need a manual patch.</summary>
    private static void PrintClosingSections(RunOutcome outcome, ReportContext context, ILogSink log)
    {
        var world = outcome.World;
        var removals = outcome.Removals;
        log.Print(LogSections.Removals(world, removals, context));
        log.Print(LogSections.Bounds(outcome.Shapes, context));
        log.Print(LogSections.Spaces(world, removals, context));
        log.Print(LogSections.TargetVisibility(world.Targets, world.Counts.TargetsHiddenOrWithoutPlacement, context));
        log.Print(LogSections.RemovalSummary(removals));
        log.Print(LogSections.KeptSummary(RemovalList.Kept(outcome.Final)));
        log.Print(LogSections.MarkersByType(world.Targets, removals, context));
        log.Print(LogSections.Hints(world, outcome.Hints));
    }

    private static void WriteReports(RunSettings options, RunResult.Done done, IReportOutput reportOutput, CachesOfTheRun caches, ReportContext context, ILogSink log)
    {
        var files = Failures.Guard(
            caches.RecordUnexpected,
            "the report files",
            "Some or all of the report files are missing; the patch is not affected.",
            () => reportOutput.Write(options.Reports, Tables.Build(done.Outcome, done.Details, options.Reports, options.LeftBehind, options.AlsoRemove, context)),
            () => new ReportFilesResult([], []));
        log.Print(LogSections.Written(files.Written, done.Details, context));
        log.Print(LogSections.ReportFileWarnings(files.Warnings));
    }

    /// <summary>Every decision of the run, from the setup through the marker moves, and the also-remove run its explanations reuse.</summary>
    private static (StepResults Results, RestingObjectsRun RestingObjects) Decide(RunSettings options, CollectedObjects world, IPluginRecords plugin, CachesOfTheRun caches, ILogSink log, ReportContext context)
    {
        log.Timed(
            () => caches.IndexObjects(world, options.Execution),
            (_, elapsed) => LogSections.Index(world, elapsed, context));
        var order = TargetWorkOrder.Of(world.Targets);
        var replacements = log.Timed(
            () => ReplacementMatcher.Find(world.Targets, caches.OtherModObjectPositions(), world.OtherModObjects.Length, caches.Shapes, order, options.Execution),
            (found, elapsed) => LogSections.Replacements(world, found, caches.TakeProblems(), elapsed, context));
        PrintUnexpectedErrors(caches, log, context);
        var links = LinkRule.DecideLinkOutcome(world.Targets, world.Links);
        var protection = ObjectsToKeep.Build(world.Targets, links.GroupLinks, links.References);
        log.Print(LogSections.Groups(protection.Groups, links.TargetObjectLinkCount, context));
        RecordTargetFailures(caches, world, protection.CheckFailures, "finding the target objects to keep", "They were kept.");
        PrintUnexpectedErrors(caches, log, context);
        var otherModObjects = caches.ObjectsThatCanCauseRemovals(replacements, options.TooClose.Npcs);

        var scope = new PhaseScope(options, world, plugin, caches, log, context);
        var tooClose = FindTooCloseObjects(scope, otherModObjects, replacements, protection, order);
        var restingObjects = RemoveRestingObjects(scope, protection, tooClose.RemovalDecisions);
        var leftBehind = DecideLeftBehind(scope, otherModObjects, order, restingObjects.Decisions);
        var markerMoves = MoveKeptMarkers(scope, leftBehind);
        var decided = new StepResults(
            world, tooClose.Census, replacements, protection, tooClose.Result, restingObjects.Run.Result, restingObjects.TouchChains,
            leftBehind.Result, markerMoves, leftBehind.Final.AddMoves(MarkerMovesOf(markerMoves)));
        return (decided, restingObjects.Run);
    }

    private static List<MarkerMove> MarkerMovesOf(MarkerMoves markerMoves) =>
        [.. markerMoves.Moved.Select(move => new MarkerMove(new TargetId(move.Evaluation.TargetIndex), move.From, move.To))];

    private static TooClosePhase FindTooCloseObjects(
        PhaseScope scope, IObjectsThatCanCauseRemovals otherModObjects, Replacements replacements, ObjectsToKeep protection, WorkOrder order)
    {
        var (options, world, caches) = (scope.Options, scope.World, scope.Caches);
        var npcs = options.TooClose.Npcs == NpcHandling.OnlyWhenStuckInObject ? caches.NpcsThatCanSpawn(replacements) : null;
        var input = new TooCloseInput(
            world.Targets, caches.VisibleTargets, order, otherModObjects, npcs, caches.Shapes, caches.Triangles, options.Execution);
        var phase = scope.Log.Timed(
            () => DecideTooClose(input, world, options.TooClose, protection, caches),
            (decided, elapsed) => LogSections.TooClose(TooCloseSectionOf(options, world, decided, caches, elapsed), scope.Context));
        scope.Log.Print(LogSections.Linked(phase.RemovalDecisions, [phase.RemovalDecisions.Rounds[^1]], "too-close", scope.Context));
        RecordTargetFailures(caches, world, phase.Result.Failures, "checking objects for being too close", "They were not removed.");
        PrintUnexpectedErrors(caches, scope.Log, scope.Context);
        return phase;
    }

    /// <remarks>The phase's mesh problems are taken before the census, whose other-mod object reads belong to the next phase.</remarks>
    private static TooClosePhase DecideTooClose(TooCloseInput input, CollectedObjects world, TooCloseOptions options, ObjectsToKeep protection, CachesOfTheRun caches)
    {
        var result = TooCloseObjects.Find(input, options);
        var decisions = RemovalDecisions.Start(protection, input.Targets.Length).Apply(RoundKind.TooClose, result.Proposals);
        var problems = caches.TakeProblems();
        return new TooClosePhase(result, decisions, problems, InvisibleOtherObjectCounter.Take(world, caches.Shapes, caches.VisibleTargets));
    }

    private static TooCloseSection TooCloseSectionOf(RunSettings options, CollectedObjects world, TooClosePhase phase, CachesOfTheRun caches, TimeSpan elapsed)
    {
        var meshes = caches.Triangles.GetStats();
        var bodies = caches.Bodies.GetStats();
        return new TooCloseSection(
            world,
            options.TargetAsTyped,
            options.TooClose,
            phase.Result,
            phase.Census,
            [.. RemovalList.KeptIn(phase.RemovalDecisions, phase.RemovalDecisions.Rounds[^1])],
            phase.Problems,
            meshes.Built,
            meshes.Triangles,
            bodies.BodiesBuilt,
            bodies.BasesResolved,
            bodies.ListsResolved,
            caches.CountBodyMeshSets(),
            caches.CountEffectOnlyMeshes(),
            elapsed);
    }

    /// <param name="afterTooClose">The decisions whose last round is the too-close round.</param>
    private static RestingObjectsPhase RemoveRestingObjects(PhaseScope scope, ObjectsToKeep protection, RemovalDecisions afterTooClose)
    {
        var (world, caches) = (scope.World, scope.Caches);
        var phase = scope.Log.Timed(
            () => FindTouchChainsOfRounds(scope, ApplyRounds(scope, protection, afterTooClose)),
            (decided, _) => decided.Run.Result.Rounds.IsEmpty
                ? new LogSection("follow-up", [])
                : RestingObjectsSection(world, decided, caches, scope.Context));
        scope.Log.Print(LogSections.Linked(phase.Decisions, phase.Run.Result.Rounds, "follow-up", scope.Context));
        PrintUnexpectedErrors(caches, scope.Log, scope.Context);
        return phase;
    }

    private static RestingObjectsPhase ApplyRounds(PhaseScope scope, ObjectsToKeep protection, RemovalDecisions afterTooClose)
    {
        var options = scope.Options;
        return Failures.Guard(
            scope.Caches.RecordUnexpected,
            "the also-remove step",
            "The also-remove rounds did not complete, so objects resting on removed ones were not removed.",
            () => ApplyRoundsOnInput(scope, CreateRestingObjectsInput(scope, protection, afterTooClose), afterTooClose),
            () =>
            {
                // The rounds that were applied before the failure are dropped, so the later steps can add to these decisions again.
                afterTooClose.DropNewerThanThisView();
                return new RestingObjectsPhase(
                    RestingObjects.Finish(NotRunPlan(options.AlsoRemove), afterTooClose, afterTooClose, []), afterTooClose, TouchChains: null, TimeSpan.Zero);
            });
    }

    private static RestingObjectsInput CreateRestingObjectsInput(PhaseScope scope, ObjectsToKeep protection, RemovalDecisions afterTooClose)
    {
        var (options, world, caches) = (scope.Options, scope.World, scope.Caches);
        return new RestingObjectsInput(
            world.Targets,
            protection,
            CollectObjectsOfAnyPluginIfSupportRoundsRun(options, caches, afterTooClose),
            caches.Shapes,
            caches.Triangles,
            caches.Terrain,
            options.Execution);
    }

    private static RestingObjectsPhase ApplyRoundsOnInput(PhaseScope scope, RestingObjectsInput input, RemovalDecisions afterTooClose)
    {
        var (run, decisions) = scope.Log.Measured(() => ApplyRestingObjectsRounds(afterTooClose, input, scope.Options.AlsoRemove), out var roundsTime);
        return new RestingObjectsPhase(run, decisions, TouchChains: null, roundsTime);
    }

    /// <remarks>Guarded on its own, so a failure here keeps the rounds that completed.</remarks>
    private static RestingObjectsPhase FindTouchChainsOfRounds(PhaseScope scope, RestingObjectsPhase rounds)
    {
        var (options, world) = (scope.Options, scope.World);
        if (!NeedsTouchChains(options, rounds.Run.Result)) return rounds;
        var touchChains = Failures.Guard<TouchChainSet?>(
            scope.Caches.RecordUnexpected,
            "the touch-chain search",
            "The chains of touching objects that the detailed log and the report files list are missing.",
            () => RestingObjects.FindTouchChains(rounds.Run, world.Targets.Length, options.Execution),
            () => null);
        return rounds with { TouchChains = touchChains };
    }

    /// <summary>The plan of rounds that did not run: without seeds, nothing is asked of the rounds.</summary>
    private static RestingObjectsPlan NotRunPlan(AlsoRemoveSettings options) =>
        new(options.Mode, HadSeeds: false, Rounds: null, Search: null);

    private static (RestingObjectsRun Run, RemovalDecisions Decisions) ApplyRestingObjectsRounds(
        RemovalDecisions afterTooClose, RestingObjectsInput input, AlsoRemoveSettings options)
    {
        var plan = RestingObjects.Prepare(afterTooClose, input, options);
        var (decisions, evidence) = plan.Rounds is { } rounds
            ? afterTooClose.ApplyRoundsUntilNothingRemoved(rounds)
            : (afterTooClose, ImmutableArray<RoundDetails>.Empty);
        return (RestingObjects.Finish(plan, afterTooClose, decisions, evidence), decisions);
    }

    /// <remarks>Only asked for when needed, because the first ask builds the supporters' bounds index.</remarks>
    private static IVisibleObjectsOfAnyPlugin? CollectObjectsOfAnyPluginIfSupportRoundsRun(RunSettings options, CachesOfTheRun caches, IRemovalDecisions afterTooClose) =>
        options.AlsoRemove.Mode == FollowUpRemovalMode.ObjectsSupportedByIt && !afterTooClose.RemovedIn(afterTooClose.Rounds[^1]).IsEmpty
            ? caches.VisibleObjectsOfAnyPlugin()
            : null;

    /// <summary>Finding the touch chains tests the touching pairs again, so only the detailed log and the report files ask for them.</summary>
    private static bool NeedsTouchChains(RunSettings options, RestingObjectsResult alsoRemove) =>
        alsoRemove.Mode == FollowUpRemovalMode.EverythingTouching
        && alsoRemove.HadSeeds
        && (options.DetailedLog || options.Reports.WriteFiles);

    private static LogSection RestingObjectsSection(
        CollectedObjects world, RestingObjectsPhase phase, CachesOfTheRun caches, ReportContext context) =>
        phase.Run.Result.Mode == FollowUpRemovalMode.EverythingTouching
            ? LogSections.Touch(world, phase.Run.Result, phase.TouchChains, caches.TakeProblems(), phase.RoundsTime, caches.Triangles.GetStats(), context)
            : LogSections.Anchoring(world, phase.Run.Result, caches.TakeProblems(), phase.RoundsTime, caches.Triangles.GetStats(), context);

    /// <param name="afterRestingObjects">The decisions after the also-remove rounds.</param>
    private static LeftBehindPhase DecideLeftBehind(PhaseScope scope, IObjectsThatCanCauseRemovals otherModObjects, WorkOrder order, RemovalDecisions afterRestingObjects)
    {
        var (options, world, caches, context) = (scope.Options, scope.World, scope.Caches, scope.Context);
        var phase = options.LeftBehind is { } leftBehindOptions
            ? scope.Log.Timed(
                () => ApplyLeftBehind(afterRestingObjects, EvaluateLeftBehind(scope, otherModObjects, order, afterRestingObjects, leftBehindOptions)),
                (evaluated, elapsed) => LogSections.LeftBehind(
                    world, evaluated.Result, RemovalList.KeptIn(evaluated.Final, evaluated.Final.Rounds[^1]), caches.TakeProblems(), elapsed, context))
            : ApplyLeftBehind(afterRestingObjects, LeftBehindResult.None);
        scope.Log.Print(LogSections.Linked(phase.Final, [phase.Final.Rounds[^1]], "leftover invisible object", context));
        PrintUnexpectedErrors(caches, scope.Log, context);
        return phase;
    }

    /// <remarks>Only the computing is guarded; adding the round to the shared decisions must not fail halfway, so a bug there ends the run.</remarks>
    private static LeftBehindResult EvaluateLeftBehind(
        PhaseScope scope, IObjectsThatCanCauseRemovals otherModObjects, WorkOrder order, RemovalDecisions afterRestingObjects, LeftBehindOptions options)
    {
        var (world, caches, execution) = (scope.World, scope.Caches, scope.Options.Execution);
        return Failures.Guard(
            caches.RecordUnexpected,
            "the left-behind step",
            "The left-behind step did not run, so invisible objects whose surroundings were removed were not removed.",
            () =>
            {
                var hosts = Hosts.Find(world.Targets, caches.Shapes, otherModObjects, order, execution);
                return LeftBehindObjects.Evaluate(
                    new LeftBehindInput(world.Targets, hosts, afterRestingObjects, caches.VisibleTargets, caches.Bases, caches.Shapes, order, execution),
                    options);
            },
            () => LeftBehindResult.None);
    }

    private static LeftBehindPhase ApplyLeftBehind(RemovalDecisions afterRestingObjects, LeftBehindResult evaluated)
    {
        var final = afterRestingObjects.Apply(RoundKind.LeftBehind, [.. LeftBehindRound.Proposals(evaluated)]);
        return new LeftBehindPhase(LeftBehindRound.WithVerdicts(evaluated, final), final);
    }

    private static MarkerMoves MoveKeptMarkers(PhaseScope scope, LeftBehindPhase leftBehind)
    {
        var (options, world, caches) = (scope.Options, scope.World, scope.Caches);
        if (options.MarkerMoves is not { } markerMoveSettings) return MarkerMoves.None;

        var markerMoves = Failures.Guard(
            caches.RecordUnexpected,
            "the marker moves",
            "No kept marker was moved, so markers inside other mods' objects stay where they are.",
            () => MarkerMovePlanner.Plan(
                new MarkerMoveInput(
                    world.Targets,
                    MarkerMovePlanner.Candidates(leftBehind.Result, leftBehind.Final),
                    new MarkerSurroundings(caches.VisibleObjectsOfAnyPlugin(), caches.VisibleTargets, leftBehind.Final),
                    scope.Plugin,
                    caches.Navmeshes,
                    caches.Terrain,
                    caches.Shapes,
                    options.Execution),
                markerMoveSettings),
            () => MarkerMoves.None);
        scope.Log.Print(LogSections.KeptMarkerMoves(world, markerMoves, markerMoveSettings, caches.TakeProblems(), scope.Context));
        PrintUnexpectedErrors(caches, scope.Log, scope.Context);
        return markerMoves;
    }

    /// <summary>The removals in the order the removal list reports them, and the moves.</summary>
    private static PatchPlan PlanPatch(StepResults decided, ImmutableArray<RemovedObject> removals) =>
        new(
            [.. removals.Select(removal => new TargetId(removal.TargetIndex))],
            [.. decided.MarkerMoves.Moved.Select(move => new MarkerMove(new TargetId(move.Evaluation.TargetIndex), move.From, move.To))]);

    /// <summary>The evidence only the report files show; none when no files are written.</summary>
    private static ReportFileDetails Explain(RunSettings options, StepResults decided, RestingObjectsRun restingObjects, CachesOfTheRun caches)
    {
        if (!options.Reports.WriteFiles) return ReportFileDetails.None;
        return Failures.Guard(
            caches.RecordUnexpected,
            "the report-file details",
            "The touching pairs or mesh origins that the report files list are missing from them.",
            () => ExplanationFinder.Compute(new ReportFileDetailsInput(
                restingObjects, decided.TouchChains, decided.World.Targets, caches.Shapes, caches.Triangles, caches.Bases, options.Execution)),
            () => ReportFileDetails.None);
    }

    /// <remarks>Taken after the explanations, so their mesh reads and problems are counted.</remarks>
    private static RunOutcome ToOutcome(StepResults decided, ImmutableArray<RemovedObject> removals, WriteSummary written, CachesOfTheRun caches)
    {
        var hints = ManualPatchHints.Hints(decided, caches.Shapes, removals);
        return new RunOutcome(decided, removals, hints, written, caches.AllProblems().Assets, caches.CountBounds());
    }

    /// <summary>What every phase function reads besides its own inputs.</summary>
    private sealed record PhaseScope(
        RunSettings Options, CollectedObjects World, IPluginRecords Plugin, CachesOfTheRun Caches, ILogSink Log, ReportContext Context);

    /// <param name="Problems">The mesh problems the phase met.</param>
    private sealed record TooClosePhase(TooCloseResult Result, RemovalDecisions RemovalDecisions, PhaseProblems Problems, InvisibleOtherObjectCounts Census);

    /// <param name="TouchChains">Null unless the touch rounds ran and the detailed log or the report files need them.</param>
    /// <param name="RoundsTime">The time of the rounds alone, without the search for the touch chains.</param>
    /// <param name="Decisions">The decisions with the also-remove rounds.</param>
    private sealed record RestingObjectsPhase(RestingObjectsRun Run, RemovalDecisions Decisions, TouchChainSet? TouchChains, TimeSpan RoundsTime);

    /// <param name="Final">The decisions with every round.</param>
    private sealed record LeftBehindPhase(LeftBehindResult Result, RemovalDecisions Final);
}
