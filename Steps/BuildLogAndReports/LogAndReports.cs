using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The log and the report files of one run. The run hands it each step's result as the step ends and prints the
/// sections it returns, unchanged and in order. It decides which sections print, words the unexpected errors,
/// and takes the mesh problems, archive problems and unexpected errors only where the log shows them.
/// </summary>
public sealed class LogAndReports
{
    private readonly RunSettings _settings;
    private readonly ILoadOrderCaches _caches;
    private readonly IUnexpectedErrors _unexpectedErrors;
    private readonly ReportContext _context;
    private readonly bool _reportFilesWritten;
    private ErrorMark _errorsTakenUpTo;
    private ProblemMark _archiveProblemsTakenUpTo;
    private ProblemMark _meshProblemsAtMeshSection;
    private ErrorMark _errorsAtMeshSection;

    /// <param name="reportFolder">What the report writer decided; the tables are built only when it is performed.</param>
    /// <param name="unexpectedErrors">The run's unexpected errors: the steps record into it, and the log only reads what is new where it shows errors; it never adds to it.</param>
    public LogAndReports(RunSettings settings, ReportFolderPrepared reportFolder, ILoadOrderCaches caches, IUnexpectedErrors unexpectedErrors)
    {
        _settings = settings;
        _reportFilesWritten = reportFolder.Outcome == StepOutcome.Performed;
        _caches = caches;
        _unexpectedErrors = unexpectedErrors;
        _context = new ReportContext(caches.Bases, caches.Shapes, settings.DetailedLog);
    }

    /// <summary>
    /// For a config file of a batch, the lines that open its run; then the warnings the settings check found, then the
    /// stop line when the request stops the run. A Standard run has no opening lines.
    /// </summary>
    public static ImmutableArray<LogSection> BuildStartSections(RunRequest request)
    {
        var warnings = LogSections.BuildWarningsSection(request.Warnings);
        var stopped = request is RunRequest.Stop stop ? [LogSections.BuildStopSection(stop.Reason, stop.Source)] : ImmutableArray<LogSection>.Empty;
        return [.. BuildConfigFileSections(request), warnings, .. stopped];
    }

    private static ImmutableArray<LogSection> BuildConfigFileSections(RunRequest request) => request switch
    {
        RunRequest.Ready { Source: SettingsSource.ConfigFile file } ready =>
            LogSections.BuildConfigFileSections(file, ready.Settings.TargetAsTyped, ready.Settings.DetailedLog),
        RunRequest.Stop { Source: SettingsSource.ConfigFile } => throw new UnreachableException("A config file that stops fails the batch before any run."),
        _ => [],
    };

    public static ImmutableArray<LogSection> BuildReportFolderSections(ReportFolderPrepared prepared) =>
        prepared.Warning is { } warning ? [LogSections.BuildReportFolderWarningSection(warning)] : [];

    public static ImmutableArray<LogSection> BuildStopSections(StopReason reason, SettingsSource source) => [LogSections.BuildStopSection(reason, source)];

    /// <summary>The problems met while reading the archive index and starting the mesh loader.</summary>
    public ImmutableArray<LogSection> BuildArchiveSections() =>
        [LogSections.BuildArchiveWarningsSection(TakeArchiveProblems()), TakeUnexpectedErrors()];

    public ImmutableArray<LogSection> BuildStepSections(IdentifiedMods mods) => [LogSections.BuildConfigSection(_settings, mods)];

    /// <remarks>The run stops when there is nothing to check, before the threads line.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(CollectedObjects world, TimeSpan elapsed)
    {
        var scan = LogSections.BuildScanSection(world, _settings.TargetAsTyped, elapsed, _context);
        return world.Stop is not null ? [scan] : [scan, LogSections.BuildThreadsSection(_settings.Execution, _context)];
    }

    public ImmutableArray<LogSection> BuildIndexSections(CollectedObjects world, TimeSpan elapsed) =>
        [LogSections.BuildIndexSection(world, elapsed, _context)];

    public ImmutableArray<LogSection> BuildStepSections(Replacements found, CollectedObjects world, TimeSpan elapsed)
    {
        return [LogSections.BuildReplacementsSection(world, found, elapsed, _context), TakeUnexpectedErrors()];
    }

    public ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepResult found)
    {
        var groups = LogSections.BuildGroupsSection(found.ObjectsToKeep.Groups, found.LinkCount, _context);
        return [groups, TakeUnexpectedErrors(found.CheckFailures)];
    }

    /// <param name="decisions">The decisions whose last round is the changed-terrain round.</param>
    /// <remarks>A switched-off step prints nothing, not even an empty section.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(ChangedTerrainResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [];

        var kept = RemovalList.KeptIn(decisions, decisions.Rounds[^1]);
        var section = LogSections.BuildChangedTerrainSection(world, _settings.TargetAsTyped, found, kept, elapsed, _context);
        return BuildRoundSections(section, decisions, RoundKind.ChangedTerrain, found.Failures);
    }

    /// <param name="decisions">The decisions whose last round is the too-close round.</param>
    /// <param name="visibleTargets">The visible target objects (C12), which the invisible other-mod objects are counted against.</param>
    public ImmutableArray<LogSection> BuildStepSections(
        TooCloseResult found, IRemovalDecisions decisions, CollectedObjects world, IVisibleTargetObjects visibleTargets, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [LogSections.BuildSwitchedOffTooCloseSection(), TakeUnexpectedErrors()];

        var census = InvisibleOtherObjectCounter.Take(world, _caches.Shapes, visibleTargets);
        var tooClose = LogSections.BuildTooCloseSection(BuildTooCloseSection(found, decisions, world, census, elapsed), _context);
        return BuildRoundSections(tooClose, decisions, RoundKind.TooClose, found.Failures);
    }

    /// <summary>The step's section, the line for the removals linked to its own, then the step's unexpected errors.</summary>
    /// <param name="decisions">The decisions whose last round is the step's round.</param>
    private ImmutableArray<LogSection> BuildRoundSections(
        LogSection section, IRemovalDecisions decisions, RoundKind kind, IEnumerable<PartFailure> failures)
    {
        var linked = LogSections.BuildLinkedSection(decisions, [decisions.Rounds[^1]], kind, _context);
        return [section, linked, TakeUnexpectedErrors(failures)];
    }

    /// <param name="decisions">The run's decisions with the also-remove rounds applied.</param>
    public ImmutableArray<LogSection> BuildStepSections(RestingObjectsResult alsoRemove, IRemovalDecisions decisions, CollectedObjects world)
    {
        var section = LogSections.BuildAlsoRemoveSection(world, alsoRemove, decisions, _caches.Triangles.GetStats(), _context);
        var linked = LogSections.BuildLinkedSection(decisions, alsoRemove.Rounds, RoundKind.AlsoRemove, _context);
        return [section, linked, TakeUnexpectedErrors(Present(alsoRemove.Failure, alsoRemove.TouchChainsFailure))];
    }

    /// <param name="final">The decisions whose last round is the left-behind round.</param>
    public ImmutableArray<LogSection> BuildStepSections(LeftBehindResult found, IRemovalDecisions final, CollectedObjects world, TimeSpan elapsed)
    {
        var lastRound = final.Rounds[^1];
        var leftBehind = LogSections.BuildLeftBehindSection(world, found, RemovalList.KeptIn(final, lastRound), elapsed, _context);
        var linked = LogSections.BuildLinkedSection(final, [lastRound], RoundKind.LeftBehind, _context);
        return [leftBehind, linked, TakeUnexpectedErrors(Present(found.Failure))];
    }

    public ImmutableArray<LogSection> BuildStepSections(MarkerMoves moves, CollectedObjects world)
    {
        var section = LogSections.BuildKeptMarkerMovesSection(world, moves, _context);
        return [section, TakeUnexpectedErrors(Present(moves.Failure))];
    }

    public ImmutableArray<LogSection> BuildStepSections(WriteSummary written, TimeSpan elapsed) => [LogSections.BuildWriteSection(written, elapsed, _context)];

    /// <summary>
    /// The end of the log before the report files: the report-file details' not-performed line, the mesh problems of
    /// the whole run, then the closing sections; and the tables of the report files.
    /// </summary>
    /// <param name="written">What the patch writer did; the log lists the removals in the order it wrote them.</param>
    /// <remarks>
    /// The mesh problems and the archive problems are taken last, after the closing sections and the tables are
    /// built, because those may still read meshes (a missing strip field is an archive problem found that way);
    /// the mesh section prints before the archive warnings and the errors.
    /// </remarks>
    public EndOfRun BuildEndOfRun(StepResults decided, WriteSummary written)
    {
        var details = decided.RestingObjects.ReportFileDetails;
        var explanationProblems = LogSections.BuildExplanationProblemsSection(details.Outcome);
        var unexpectedErrors = TakeUnexpectedErrors(Present(details.Failure));
        var outcome = BuildOutcome(decided, written);
        var closingSections = BuildClosingSections(outcome);
        var tables = BuildReportTables(outcome);
        var meshProblems = TakeMeshProblems();
        var archiveWarnings = LogSections.BuildArchiveWarningsSection(TakeArchiveProblems());
        return new EndOfRun([explanationProblems, meshProblems, archiveWarnings, unexpectedErrors, .. closingSections], tables, outcome);
    }

    /// <summary>The last sections, after the report files: what was written, the writer's warnings, the errors since the last take and the final line.</summary>
    /// <param name="ended">The end of the run the report files were written for.</param>
    /// <param name="files">What the report writer did.</param>
    /// <param name="timeSinceStart">The time since the program started, for the final line.</param>
    public ImmutableArray<LogSection> BuildFilesWrittenSections(EndOfRun ended, ReportFilesResult files, TimeSpan timeSinceStart)
    {
        RequireNoProblemRecordedSinceEndOfRunTakes();
        return
        [
            LogSections.BuildWrittenSection(files, ended.Outcome.RestingObjects, _context),
            LogSections.BuildReportFileWarningsSection(files.Warnings),
            TakeUnexpectedErrors(Present(ended.Tables.Failure, files.Failure)),
            LogSections.BuildDoneSection(timeSinceStart, _context),
        ];
    }

    /// <remarks>
    /// A failure building the tables does not end the run. It is worded with the files-written sections, where the
    /// writer's own failure is, so the error line prints there.
    /// </remarks>
    private ReportTables BuildReportTables(RunOutcome outcome)
    {
        if (!_reportFilesWritten) return ReportTables.SwitchedOff;

        return Failures.Attempt(
            () => new ReportTables(StepOutcome.Performed, Tables.BuildTables(outcome, _settings.AlsoRemove, _context), Failure: null),
            failure => new ReportTables(StepOutcome.StoppedByUnexpectedError, [], PartFailure.OfWholePart(ReportFileParts.ReportFiles, failure)));
    }

    /// <summary>The errors recorded since the last take, worded, except the mesh errors, which print in the mesh problems section.</summary>
    private LogSection TakeUnexpectedErrors() => TakeUnexpectedErrors([]);

    /// <param name="resultFailures">The failures the step result just handed over; they are worded together with the channel's, as one set.</param>
    private LogSection TakeUnexpectedErrors(IEnumerable<PartFailure> resultFailures)
    {
        var channelFailures = _unexpectedErrors.Since(_errorsTakenUpTo).Where(failure => !IsAboutMesh(failure));
        _errorsTakenUpTo = _unexpectedErrors.Mark();
        return LogSections.BuildUnexpectedErrorsSection(resultFailures.Concat(channelFailures).Select(RunPartText.Word), _context);
    }

    /// <summary>Every mesh problem and mesh error of the run.</summary>
    private LogSection TakeMeshProblems()
    {
        var problems = _caches.MeshFiles.Problems;
        _meshProblemsAtMeshSection = problems.Mark();
        _errorsAtMeshSection = _unexpectedErrors.Mark();
        var meshErrors = _unexpectedErrors.Since(default).Where(IsAboutMesh).Select(failure => WithPrintedMesh(failure, problems));
        return LogSections.BuildMeshProblemsSection(problems.Since(default), meshErrors.Select(RunPartText.Word), _context);
    }

    private static PartFailure WithPrintedMesh(PartFailure failure, IMeshProblems problems) =>
        failure with { Subject = failure.Subject! with { Key = problems.PrintedSpelling(failure.Subject!.Key) } };

    /// <remarks>Nothing reads meshes after the report tables are built, so a mesh or archive problem recorded since the end takes is a bug in the run.</remarks>
    private void RequireNoProblemRecordedSinceEndOfRunTakes()
    {
        var problems = _caches.MeshFiles.Problems;
        var lateProblems = problems.Since(_meshProblemsAtMeshSection);
        var lateErrors = _unexpectedErrors.Since(_errorsAtMeshSection).Where(IsAboutMesh);
        if (lateProblems.Length > 0 || lateErrors.Any() || problems.ArchiveProblemsSince(_archiveProblemsTakenUpTo).Length > 0)
        {
            throw new UnreachableException("A mesh problem, a mesh error or an archive problem was recorded after the end of the run took them.");
        }
    }

    /// <remarks>The loader start-up is a mesh-reading part too, but it is about no mesh, so it prints with the other errors.</remarks>
    private static bool IsAboutMesh(PartFailure failure) => failure.Part == MeshReadingParts.MeshShapes || failure.Part == MeshReadingParts.MeshTriangles;

    /// <summary>The archive and data-folder problems met since the last take; each is handed out once.</summary>
    private ImmutableArray<ArchiveProblem> TakeArchiveProblems()
    {
        var problems = _caches.MeshFiles.Problems;
        var taken = problems.ArchiveProblemsSince(_archiveProblemsTakenUpTo);
        _archiveProblemsTakenUpTo = problems.Mark();
        return taken;
    }

    /// <param name="failures">The failures a result stopped its parts with; null where there was none.</param>
    private static IEnumerable<PartFailure> Present(params PartFailure?[] failures) => failures.OfType<PartFailure>();

    private TooCloseSection BuildTooCloseSection(
        TooCloseResult found, IRemovalDecisions decisions, CollectedObjects world, InvisibleOtherObjectCounts census, TimeSpan elapsed)
    {
        var meshes = _caches.Triangles.GetStats();
        var bodies = _caches.Bodies.GetStats();
        return new TooCloseSection(
            world,
            _settings.TargetAsTyped,
            found,
            census,
            [.. RemovalList.KeptIn(decisions, decisions.Rounds[^1])],
            meshes.Built,
            meshes.Triangles,
            bodies.BodiesBuilt,
            bodies.BasesResolved,
            bodies.ListsResolved,
            _caches.CountBodyMeshSets(),
            _caches.CountEffectOnlyMeshes(),
            elapsed,
            CountRemovedBeforeThisRound(found, decisions));
    }

    private static int CountRemovedBeforeThisRound(TooCloseResult found, IRemovalDecisions decisions)
    {
        var thisRound = decisions.Rounds[^1];
        return found.Hits.Count(hit => decisions.Of(new TargetId(hit.TargetIndex)) is Decision.Removed { Round: var round } && round != thisRound);
    }

    private RunOutcome BuildOutcome(StepResults decided, WriteSummary written)
    {
        ImmutableArray<RemovedObject> removals = [.. RemovalList.Removals(written.Removed, decided.Final, decided.Collected, decided.LeftBehind)];
        var hints = ManualPatchHints.FindManualPatchHints(decided, _caches.Shapes, removals);
        return new RunOutcome(decided, removals, hints, written, _caches.CountBounds());
    }

    /// <summary>What was removed and kept, the removals that can still affect the game, and what may need a manual patch.</summary>
    /// <remarks>Like the report-folder warning, the removal warnings print only when there is one.</remarks>
    private ImmutableArray<LogSection> BuildClosingSections(RunOutcome outcome)
    {
        var world = outcome.Collected;
        var removals = outcome.Removals;
        var warnings = LogSections.BuildRemovalWarningsSection(world, removals, outcome.RemovalRisks);
        var kept = RemovalList.Kept(outcome.Final);
        var keptOnPurpose = LogSections.BuildKeptOnPurposeSection(world, kept);
        var keptSummary = LogSections.BuildKeptSummarySection(kept);
        var markersByType = LogSections.BuildMarkersByTypeSection(world.Targets, removals, _context);
        return
        [
            LogSections.BuildRemovalsSection(world, removals, _context),
            LogSections.BuildBoundsSection(outcome.Shapes, _caches.MeshFiles.CountTriangleFailures(), _context),
            LogSections.BuildSpacesSection(world, removals, _context),
            LogSections.BuildTargetVisibilitySection(world.Targets, world.Counts.TargetsHiddenOrWithoutPlacement, _context),
            LogSections.BuildRemovalSummarySection(removals, outcome.ChangedTerrain.Outcome),
            .. keptSummary.Lines.IsEmpty ? ImmutableArray<LogSection>.Empty : [keptSummary],
            .. keptOnPurpose.Lines.IsEmpty ? ImmutableArray<LogSection>.Empty : [keptOnPurpose],
            .. markersByType.Lines.IsEmpty ? ImmutableArray<LogSection>.Empty : [markersByType],
            .. warnings.Lines.IsEmpty ? ImmutableArray<LogSection>.Empty : [warnings],
            LogSections.BuildHintsSection(world, outcome.Hints),
        ];
    }
}
