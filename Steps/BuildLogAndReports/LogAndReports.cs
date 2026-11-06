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
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
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

    /// <summary>Why the restored parts of the earlier copy of this patcher are not used; nothing when they are, or when there are none to use.</summary>
    public static ImmutableArray<LogSection> BuildRestoredPartsFileSections(RestoredPartsFileProblem? problem) =>
        problem is null ? [] : [LogSections.BuildRestoredPartsFileSection(problem)];

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
            : [LogSections.BuildHeldUpSection(found, _settings.TargetAsTyped, elapsed, _context), TakeUnexpectedErrors(PartFailure.Present(found.Failure))];

    /// <param name="decisions">The run's decisions with the step's rounds applied.</param>
    /// <remarks>A switched-off part prints nothing, not even an empty section; without rounds there is no linked line.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(OtherModsObjectsResult settled, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed) =>
        settled.Outcome == StepOutcome.SwitchedOff
            ? []
            :
            [
                LogSections.BuildLeftWithoutSupportStepSection(world, settled, decisions, _settings.TargetAsTyped, elapsed, _context),
                .. LinkedSectionWhenAnyRound(settled.Rounds, decisions),
                TakeUnexpectedErrors(PartFailure.Present(settled.Failure)),
            ];

    private ImmutableArray<LogSection> LinkedSectionWhenAnyRound(ImmutableArray<Round> rounds, IRemovalDecisions decisions) =>
        rounds.IsEmpty ? [] : [LogSections.BuildLinkedSection(decisions, rounds, RoundKind.AlsoRemove, _context)];

    public ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepResult found, TimeSpan elapsed)
    {
        var groups = LogSections.BuildGroupsSection(found.ObjectsToKeep.Groups, found.LinkCount, _settings.TargetAsTyped, _context);
        return [WithStepTime(groups, LogSections.ObjectsToKeepStepName, elapsed), TakeUnexpectedErrors(found.CheckFailures)];
    }

    /// <summary>What other mods' objects rest on, then the objects to keep, each part with its own time.</summary>
    public ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepFound found) =>
        [.. BuildStepSections(found.HeldUp, found.HeldUpTime), .. BuildStepSections(found.Kept, found.KeepTime)];

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
    public ImmutableArray<LogSection> BuildStepSections(TooCloseResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [LogSections.BuildSwitchedOffTooCloseSection(), TakeUnexpectedErrors()];

        var tooClose = LogSections.BuildTooCloseSection(BuildTooCloseSection(found, decisions, world, elapsed), _context);
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
        [.. BuildVisibleLeftBehindSections(found, decisions, world, elapsed), .. BuildFollowUpSections(LogSections.AlsoRemovePass.AfterVisibleLeftBehind, found.FollowUp, decisions, world)];

    /// <remarks>A switched-off step prints nothing, not even an empty section.</remarks>
    private ImmutableArray<LogSection> BuildVisibleLeftBehindSections(VisibleLeftBehindResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [];

        var kept = found.Rounds.SelectMany(round => RemovalList.KeptIn(decisions, round));
        var settings = _settings.VisibleLeftBehind ?? throw new UnreachableException("The visible left-behind step ran without its settings.");
        var section = LogSections.BuildVisibleLeftBehindSection(world, found, kept, settings.MinObjectsAround, elapsed, _context);
        return BuildRoundSections(section, decisions, found.Rounds, RoundKind.VisibleLeftBehind, PartFailure.Present(found.Failure));
    }

    /// <summary>The step's section, then those of the also-remove follow-up of its round.</summary>
    /// <param name="decisions">The decisions that include the step's round and its follow-up rounds.</param>
    /// <remarks>The follow-up's unexpected errors print as those of the visible step's follow-ups do (see the visible step's sections).</remarks>
    public ImmutableArray<LogSection> BuildStepSections(ObjectsInClearedAreasResult found, IRemovalDecisions decisions, CollectedObjects world) =>
        [.. BuildObjectsInClearedAreasSections(found, decisions, world), .. BuildFollowUpSections(LogSections.AlsoRemovePass.AfterClearedAreas, found.FollowUp, decisions, world)];

    /// <remarks>A switched-off step prints nothing, and a section without a line is left out, not printed empty.</remarks>
    private ImmutableArray<LogSection> BuildObjectsInClearedAreasSections(ObjectsInClearedAreasResult found, IRemovalDecisions decisions, CollectedObjects world)
    {
        if (found.Outcome == StepOutcome.SwitchedOff) return [];

        var rounds = decisions.Rounds.Where(round => round.Kind == RoundKind.ClearedArea).ToList();
        var settings = _settings.ObjectsInClearedAreas ?? throw new UnreachableException("The step for objects in cleared areas ran without its settings.");
        var section = LogSections.BuildObjectsInClearedAreasSection(
            world, found, rounds.SelectMany(round => RemovalList.KeptIn(decisions, round)), settings.MinObjectsAround, _context);
        return [.. WhenItHasLines(section), .. BuildRoundSections(decisions, rounds, RoundKind.ClearedArea, PartFailure.Present(found.Failure))];
    }

    /// <summary>The step's section, the line for the removals linked to its own, then the step's unexpected errors.</summary>
    /// <param name="rounds">The step's rounds in <paramref name="decisions"/>.</param>
    private ImmutableArray<LogSection> BuildRoundSections(
        LogSection section, IRemovalDecisions decisions, IEnumerable<Round> rounds, RoundKind kind, IEnumerable<PartFailure> failures) =>
        [section, .. BuildRoundSections(decisions, rounds, kind, failures)];

    /// <summary>The line for the removals linked to the step's own, then the step's unexpected errors.</summary>
    private ImmutableArray<LogSection> BuildRoundSections(IRemovalDecisions decisions, IEnumerable<Round> rounds, RoundKind kind, IEnumerable<PartFailure> failures) =>
        [LogSections.BuildLinkedSection(decisions, rounds, kind, _context), TakeUnexpectedErrors(failures)];

    /// <param name="final">The decisions whose last round is the left-behind round.</param>
    public ImmutableArray<LogSection> BuildStepSections(InvisibleLeftBehindResult found, IRemovalDecisions final, CollectedObjects world, TimeSpan elapsed)
    {
        var lastRound = final.Rounds[^1];
        var leftBehind = LogSections.BuildInvisibleLeftBehindSection(world, found, RemovalList.KeptIn(final, lastRound), elapsed, _context);
        return BuildRoundSections(leftBehind, final, [lastRound], RoundKind.InvisibleLeftBehind, PartFailure.Present(found.Failure));
    }

    public ImmutableArray<LogSection> BuildStepSections(MarkerMoves moves, CollectedObjects world, TimeSpan elapsed)
    {
        var section = LogSections.BuildKeptMarkerMovesSection(world, moves, _context);
        return [WithStepTimeWhenPerformed(section, moves.Outcome, LogSections.MarkerMovesName, elapsed), TakeUnexpectedErrors(PartFailure.Present(moves.Failure))];
    }

    /// <remarks>A switched-off step prints nothing, and a section without a line is left out, not printed empty.</remarks>
    public ImmutableArray<LogSection> BuildStepSections(RestoreWhereRemovedResult found, CollectedObjects world, TimeSpan elapsed) =>
        found.Outcome == StepOutcome.SwitchedOff
            ? []
            :
            [
                .. WhenItHasLines(LogSections.BuildRestoreWhereRemovedSection(world, found, elapsed, _context)),
                .. WhenItHasLines(TakeUnexpectedErrors(found.Failures)),
            ];

    public ImmutableArray<LogSection> BuildStepSections(WriteSummary written, TimeSpan elapsed) => [LogSections.BuildWriteSection(written, elapsed, _context)];

    private LogSection WithStepTime(LogSection section, string stepName, TimeSpan elapsed) =>
        LogSections.WithStepTime(section, stepName, elapsed, _context);

    /// <summary>The step-time line only for a performed step: a switched-off step does nothing, and a stopped one says so instead.</summary>
    private LogSection WithStepTimeWhenPerformed(LogSection section, StepOutcome outcome, string stepName, TimeSpan elapsed) =>
        outcome == StepOutcome.Performed ? WithStepTime(section, stepName, elapsed) : section;

    /// <summary>The section alone, or nothing when it has no line: such a section is left out, not printed empty.</summary>
    private static ImmutableArray<LogSection> WhenItHasLines(LogSection section) => section.Lines.IsEmpty ? [] : [section];
}
