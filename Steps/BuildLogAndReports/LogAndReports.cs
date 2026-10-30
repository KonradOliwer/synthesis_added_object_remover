using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The log and the report files of one run. The run hands it each step's result as the step ends and prints the
/// sections it returns, unchanged and in order. It decides which sections print, words the unexpected errors,
/// and takes the mesh problems, archive problems and unexpected errors only where the log shows them.
/// </summary>
public sealed partial class LogAndReports
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
    public LogAndReports(
        RunSettings settings,
        ReportFolderPrepared reportFolder,
        ILoadOrderCaches caches,
        IUnexpectedErrors unexpectedErrors)
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
    public ImmutableArray<LogSection> BuildArchiveSections(TimeSpan elapsed) =>
        [WithStepTime(LogSections.BuildArchiveWarningsSection(TakeArchiveProblems()), LogSections.MeshReadingStepName, elapsed), TakeUnexpectedErrors()];

    public ImmutableArray<LogSection> BuildStepSections(ModTrustTable trust, LoadOrderPlugins plugins, TimeSpan elapsed) =>
        [WithStepTime(LogSections.BuildConfigSection(_settings, trust, plugins), LogSections.ModsStepName, elapsed)];

    /// <param name="scanElapsed">The time of the placed-record scan, which the scan line holds.</param>
    /// <param name="linksElapsed">The time of reading the links from records other than placed objects.</param>
    /// <remarks>The run stops when there is nothing to check, before the threads line; it still logs its starting state, having removed nothing.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(CollectedObjects world, TimeSpan scanElapsed, TimeSpan linksElapsed)
    {
        var scan = WithStepTime(
            LogSections.BuildScanSection(world, _settings.TargetAsTyped, scanElapsed, _context), LogSections.NonPlacedLinksStepName, linksElapsed);
        return world.Stop is not null
            ? [scan, LogSections.BuildRunSummarySection(world, _settings.TargetAsTyped, removedInThisRun: 0)]
            : [scan, LogSections.BuildThreadsSection(_settings.Execution, _context)];
    }

    public ImmutableArray<LogSection> BuildIndexSections(CollectedObjects world, TimeSpan elapsed) =>
        [LogSections.BuildIndexSection(world, elapsed, _context)];

    public ImmutableArray<LogSection> BuildStepSections(Replacements found, CollectedObjects world, TimeSpan elapsed)
    {
        return [LogSections.BuildReplacementsSection(world, found, elapsed, _context), TakeUnexpectedErrors()];
    }

    /// <remarks>A switched-off part prints nothing, not even an empty section.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(HeldUpTargets found, TimeSpan elapsed) =>
        found.Outcome == StepOutcome.SwitchedOff
            ? []
            : [LogSections.BuildHeldUpSection(found, _settings.TargetAsTyped, elapsed, _context), TakeUnexpectedErrors(Present(found.Failure))];

    /// <param name="decisions">The run's decisions with the step's rounds applied.</param>
    /// <remarks>A switched-off part prints nothing, not even an empty section; without rounds there is no linked line.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(OtherModsObjectsResult settled, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed) =>
        settled.Outcome == StepOutcome.SwitchedOff
            ? []
            :
            [
                LogSections.BuildLeftWithoutSupportStepSection(world, settled, decisions, _settings.TargetAsTyped, elapsed, _context),
                .. LinkedSectionWhenAnyRound(settled.Rounds, decisions),
                TakeUnexpectedErrors(Present(settled.Failure)),
            ];

    private ImmutableArray<LogSection> LinkedSectionWhenAnyRound(ImmutableArray<Round> rounds, IRemovalDecisions decisions) =>
        rounds.IsEmpty ? [] : [LogSections.BuildLinkedSection(decisions, rounds, RoundKind.AlsoRemove, _context)];

    public ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepResult found, TimeSpan elapsed)
    {
        var groups = LogSections.BuildGroupsSection(found.ObjectsToKeep.Groups, found.LinkCount, _settings.TargetAsTyped, _context);
        return [WithStepTime(groups, LogSections.ObjectsToKeepStepName, elapsed), TakeUnexpectedErrors(found.CheckFailures)];
    }

    /// <param name="oldPlaceWorld">The objects the old-place checks were made among; null when no check was made.</param>
    /// <remarks>
    /// A step that checked no old place prints nothing, not even an empty section. The checks' own cache failures land in the shared
    /// channel, so they print here, under the step's line.
    /// </remarks>
    public ImmutableArray<LogSection> BuildStepSections(ChangedObjectJudgingResult judged, CollectedObjects world, CollectedObjects? oldPlaceWorld, TimeSpan elapsed) =>
        judged.OldPlaceChecks.IsEmpty
            ? []
            :
            [
                LogSections.BuildOldPlaceChecksSection(world, judged, oldPlaceWorld, _settings.TargetAsTyped, elapsed, _context),
                TakeUnexpectedErrors(judged.Failures),
            ];

    public ImmutableArray<LogSection> BuildStepSections(FitResult found, TimeSpan elapsed) =>
        [LogSections.BuildFitSection(found, elapsed, _context), TakeUnexpectedErrors(found.Failures)];

    /// <param name="decisions">The decisions whose last round is the changed-terrain round.</param>
    /// <remarks>A switched-off step prints nothing, not even an empty section.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(ChangedTerrainResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [];

        var kept = RemovalList.KeptIn(decisions, decisions.Rounds[^1]);
        var section = LogSections.BuildChangedTerrainSection(world, _settings.TargetAsTyped, found, decisions, kept, elapsed, _context);
        return BuildRoundSections(section, decisions, [decisions.Rounds[^1]], RoundKind.ChangedTerrain, found.Failures);
    }

    /// <param name="decisions">The decisions whose last round is the too-close round.</param>
    /// <param name="visibleTargets">The visible target objects (C12), which the invisible other-mod objects are counted against.</param>
    public ImmutableArray<LogSection> BuildStepSections(
        TooCloseResult found, IRemovalDecisions decisions, CollectedObjects world, IVisibleTargetObjects visibleTargets, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [LogSections.BuildSwitchedOffTooCloseSection(), TakeUnexpectedErrors()];

        var census = InvisibleOtherObjectCounter.Take(world, _caches.Shapes, visibleTargets);
        var tooClose = LogSections.BuildTooCloseSection(BuildTooCloseSection(found, decisions, world, census, elapsed), _context);
        return BuildRoundSections(tooClose, decisions, [decisions.Rounds[^1]], RoundKind.TooClose, found.Failures);
    }

    /// <summary>The visible left-behind step's sections, then those of the also-remove follow-up of its rounds.</summary>
    /// <param name="decisions">The decisions that include the visible left-behind rounds and their follow-up rounds.</param>
    /// <param name="elapsed">The whole step's time as the run measured it, the follow-ups included; it is the time of the step's summary line only.</param>
    /// <remarks>
    /// The follow-ups run inside the step, so the unexpected errors the caches recorded while it ran print under the step, not under the
    /// follow-up's sections, which print the follow-up's own failure; the log cannot tell which round recorded a cache error.
    /// </remarks>
    public ImmutableArray<LogSection> BuildStepSections(VisibleLeftBehindResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed) =>
        [.. BuildVisibleLeftBehindSections(found, decisions, world, elapsed), .. BuildFollowUpAfterVisibleLeftBehindSections(found.FollowUp, decisions, world)];

    /// <remarks>A switched-off step prints nothing, not even an empty section.</remarks>
    private ImmutableArray<LogSection> BuildVisibleLeftBehindSections(VisibleLeftBehindResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [];

        var kept = found.Rounds.SelectMany(round => RemovalList.KeptIn(decisions, round));
        var section = LogSections.BuildVisibleLeftBehindSection(world, found, kept, elapsed, _context);
        return BuildRoundSections(section, decisions, found.Rounds, RoundKind.VisibleLeftBehind, Present(found.Failure));
    }

    /// <summary>The step's section, the line for the removals linked to its own, then the step's unexpected errors.</summary>
    /// <param name="rounds">The step's rounds in <paramref name="decisions"/>.</param>
    private ImmutableArray<LogSection> BuildRoundSections(
        LogSection section, IRemovalDecisions decisions, IEnumerable<Round> rounds, RoundKind kind, IEnumerable<PartFailure> failures)
    {
        var linked = LogSections.BuildLinkedSection(decisions, rounds, kind, _context);
        return [section, linked, TakeUnexpectedErrors(failures)];
    }

    /// <param name="final">The decisions whose last round is the left-behind round.</param>
    public ImmutableArray<LogSection> BuildStepSections(LeftBehindResult found, IRemovalDecisions final, CollectedObjects world, TimeSpan elapsed)
    {
        var lastRound = final.Rounds[^1];
        var leftBehind = LogSections.BuildLeftBehindSection(world, found, RemovalList.KeptIn(final, lastRound), elapsed, _context);
        return BuildRoundSections(leftBehind, final, [lastRound], RoundKind.LeftBehind, Present(found.Failure));
    }

    public ImmutableArray<LogSection> BuildStepSections(MarkerMoves moves, CollectedObjects world, TimeSpan elapsed)
    {
        var section = LogSections.BuildKeptMarkerMovesSection(world, moves, _context);
        return [WithStepTimeWhenPerformed(section, moves.Outcome, LogSections.MarkerMovesName, elapsed), TakeUnexpectedErrors(Present(moves.Failure))];
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
            LogSections.BuildWrittenSection(files, ended.Outcome.RestingObjects, _settings.TargetAsTyped, _context),
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

    private LogSection WithStepTime(LogSection section, string stepName, TimeSpan elapsed) =>
        LogSections.WithStepTime(section, stepName, elapsed, _context);

    /// <summary>The step-time line only for a performed step: a switched-off step does nothing, and a stopped one says so instead.</summary>
    private LogSection WithStepTimeWhenPerformed(LogSection section, StepOutcome outcome, string stepName, TimeSpan elapsed) =>
        outcome == StepOutcome.Performed ? WithStepTime(section, stepName, elapsed) : section;

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
            [.. found.Passes.Where(pass => pass.Outcome == StepOutcome.Performed).Select(pass => new TooClosePassReport(
                pass,
                CountRemovedBeforeThisRound(pass, decisions),
                RemovalList.CountRestored(decisions, pass.Hits.Select(hit => new TargetId(hit.TargetIndex)))))],
            census,
            [.. RemovalList.KeptIn(decisions, decisions.Rounds[^1])],
            meshes.Built,
            meshes.Triangles,
            bodies.BodiesBuilt,
            bodies.BasesResolved,
            bodies.ListsResolved,
            _caches.CountBodyMeshSets(),
            _caches.CountEffectOnlyMeshes(),
            elapsed);
    }

    private static int CountRemovedBeforeThisRound(TooClosePass found, IRemovalDecisions decisions)
    {
        var thisRound = decisions.Rounds[^1];
        return found.Hits
            .Select(hit => new TargetId(hit.TargetIndex))
            .Count(target => decisions.IsRemoved(target) && decisions.Of(target)!.Round != thisRound);
    }

    private RunOutcome BuildOutcome(StepResults decided, WriteSummary written)
    {
        ImmutableArray<ObjectWithReason> removals = [.. RemovalList.WithReasons(written.Removed, decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.LeftBehind)];
        ImmutableArray<ObjectWithReason> restorations = [.. RemovalList.WithReasons(written.Restored, decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.LeftBehind)];
        var hints = ManualPatchHints.FindManualPatchHints(decided, _caches.Shapes, removals);
        return new RunOutcome(decided, removals, restorations, hints, written, _caches.CountBounds());
    }

    /// <summary>
    /// What was removed and kept, the run's starting state and result, the removals that can still affect the game, what may
    /// need a manual patch, and where all of these are.
    /// </summary>
    /// <remarks>Like the report-folder warning, the removal warnings print only when there is one.</remarks>
    private ImmutableArray<LogSection> BuildClosingSections(RunOutcome outcome)
    {
        var world = outcome.Collected;
        var removals = outcome.Removals;
        var restoredObjects = LogSections.BuildRestoredSection(world, outcome.Restorations, outcome.Final, _context);
        var restoreSummary = LogSections.BuildRestoreSummarySection(
            outcome.Restorations, outcome.Final, _settings.TargetAsTyped, outcome.ChangedTerrain.Outcome, outcome.VisibleLeftBehind.Outcome);
        var warnings = LogSections.BuildRemovalWarningsSection(world, removals, outcome.RemovalRisks);
        var kept = RemovalList.Kept(outcome.Final);
        var keptOnPurpose = LogSections.BuildKeptOnPurposeSection(world, kept);
        var keptSummary = LogSections.BuildKeptSummarySection(kept);
        var markersByType = LogSections.BuildMarkersByTypeSection(world.Targets, removals, _context);
        var hints = LogSections.BuildHintsSection(world, outcome.Hints);
        var changedObjects = LogSections.BuildChangedObjectsSection(world, outcome.Final, _settings.TargetAsTyped, _context);
        var ownShrunkOrTurned = LogSections.BuildOwnObjectsShrunkOrTurnedSection(world, outcome.Final, _settings.TargetAsTyped, _context);
        var leftWithoutSupport = LogSections.BuildLeftWithoutSupportSection(world, outcome.OtherModsObjects, _context);
        var ownRemoved = TargetOrigins.CountOwn(outcome.Written.Removed.Select(id => world[id]));
        return
        [
            LogSections.BuildRemovalsSection(world, removals, outcome.Final, _context),
            .. WhenItHasLines(restoredObjects),
            LogSections.BuildBoundsSection(outcome.Shapes, _caches.MeshFiles.CountTriangleFailures(), _context),
            LogSections.BuildSpacesSection(world, removals, _settings.TargetAsTyped, _context),
            LogSections.BuildTargetVisibilitySection(world.Targets, world.Counts.TargetsHiddenOrWithoutPlacement, _settings.TargetAsTyped, _context),
            LogSections.BuildRunSummarySection(world, _settings.TargetAsTyped, ownRemoved),
            LogSections.BuildRemovalSummarySection(world, removals, outcome.Final, _settings.TargetAsTyped, outcome.ChangedTerrain.Outcome, outcome.VisibleLeftBehind.Outcome),
            .. WhenItHasLines(restoreSummary),
            .. WhenItHasLines(changedObjects),
            .. WhenItHasLines(ownShrunkOrTurned),
            .. WhenItHasLines(leftWithoutSupport),
            .. WhenItHasLines(keptSummary),
            .. WhenItHasLines(keptOnPurpose),
            .. WhenItHasLines(markersByType),
            .. WhenItHasLines(warnings),
            .. WhenItHasLines(hints),
            LogSections.BuildAreasSection(world, removals, kept, outcome.Hints, outcome.RemovalRisks),
        ];
    }

    /// <summary>The section alone, or nothing when it has no line: such a section is left out, not printed empty.</summary>
    private static ImmutableArray<LogSection> WhenItHasLines(LogSection section) => section.Lines.IsEmpty ? [] : [section];
}
