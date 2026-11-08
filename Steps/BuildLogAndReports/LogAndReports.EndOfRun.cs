using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

public sealed partial class LogAndReports
{
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
        var userDataFailures = TakeUserDataFailures(PartFailure.Present(details.Failure));
        var outcome = BuildOutcome(decided, written);
        var closingSections = BuildClosingSections(outcome);
        var tables = BuildReportTables(outcome);
        var meshProblems = TakeMeshProblems();
        var archiveWarnings = LogSections.BuildArchiveWarningsSection(TakeArchiveProblems());
        return new EndOfRun([explanationProblems, meshProblems, archiveWarnings, userDataFailures, .. closingSections], tables, outcome);
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
            TakeUserDataFailures(PartFailure.Present(ended.Tables.Failure, files.Failure)),
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
            failure => new ReportTables(StepOutcome.StoppedByUserDataFailure, [], PartFailure.OfWholePart(ReportFileParts.ReportFiles, failure)));
    }

    private RunOutcome BuildOutcome(StepResults decided, WriteSummary written)
    {
        ImmutableArray<ObjectWithReason> removals = [.. RemovalList.WithReasons(written.Removed, decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.InvisibleLeftBehind)];
        ImmutableArray<ObjectWithReason> restorations = [.. RemovalList.WithReasons(written.Restored, decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.InvisibleLeftBehind)];
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
        var restoreSummary = LogSections.BuildRestoreSummarySection(outcome.Restorations, outcome.Final, _settings.TargetAsTyped, SummarizedSteps.Of(outcome));
        var restoredWhereRemoved = LogSections.BuildRestoreWhereRemovedSummarySection(outcome.RestoreWhereRemoved);
        var warnings = LogSections.BuildRemovalWarningsSection(world, removals, outcome.RemovalRisks);
        var kept = RemovalList.Kept(outcome.Final);
        var keptOnPurpose = LogSections.BuildKeptOnPurposeSection(world, kept);
        var keptSummary = LogSections.BuildKeptSummarySection(kept);
        var markersByType = LogSections.BuildMarkersByTypeSection(world.Targets, removals, _context);
        var hints = LogSections.BuildHintsSection(world, outcome.Hints);
        var whereRemoved = FitOutcomes.WhereRemovedOutcomes(outcome.RestoreWhereRemoved);
        var changedObjects = LogSections.BuildChangedObjectsSection(world, outcome.Final, whereRemoved, _settings.TargetAsTyped, _context);
        var ownShrunkOrTurned = LogSections.BuildOwnObjectsShrunkOrTurnedSection(world, outcome.Final, whereRemoved, _settings.TargetAsTyped, _context);
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
            LogSections.BuildRemovalSummarySection(world, removals, outcome.Final, _settings.TargetAsTyped, SummarizedSteps.Of(outcome)),
            .. WhenItHasLines(restoreSummary),
            .. WhenItHasLines(restoredWhereRemoved),
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
}
