using System.Collections.Immutable;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>An error nobody planned for stops only the part it happened in: it is reported with its scope and the patch is still written.</summary>
public class UnexpectedErrorRunTests
{
    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static RunOutcome Run(RecordingLogSink log, Func<IPluginRecords, IPluginRecords>? wrapPlugin = null) =>
        ((RunResult.Done)FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves), workers: 1, log, DefaultMeshFiles, wrapPlugin)).Outcome;

    [Fact]
    public void AFailingMarkerMoveStepIsReportedWithItsScopeAndThePatchIsStillWritten()
    {
        var healthy = Run(new RecordingLogSink());
        var log = new RecordingLogSink();

        var outcome = Run(log, inner => new FailingHomeCellPlugin(inner));

        Assert.True(healthy.Written.Moved > 0);
        Assert.Equal(0, outcome.Written.Moved);
        Assert.NotEmpty(outcome.Removals);
        Assert.Equal(healthy.Removals.Select(removal => removal.TargetIndex), outcome.Removals.Select(removal => removal.TargetIndex));
        Assert.Equal(outcome.Removals.Select(removal => removal.TargetIndex), outcome.Written.Removed.Select(id => id.Index));
        var errorSection = Assert.Single(log.Sections, section => section.Id == "unexpectedErrors" && section.Lines.Length > 0);
        Assert.Equal(
            $"  Unexpected error in the marker moves (InvalidOperationException: {FailingHomeCellPlugin.FailureMessage}). "
            + "No kept marker was moved, so markers inside other mods' objects stay where they are.",
            Assert.Single(errorSection.Lines, IsErrorLine));
        Assert.Contains("Moved kept markers: not performed because of an unexpected error.", SectionLines(log, "relocations"));
    }

    /// <remarks>Failing the clock reading at the end of the rounds fails the step after its rounds were decided.</remarks>
    [Fact]
    public void AnAlsoRemoveFailureDuringItsRoundsAppliesNothingAndTheLaterStepsSeeTheTooCloseDecisions()
    {
        var healthy = Run(new RecordingLogSink());
        var clock = new AlsoRemoveRoundsEndClock(() => throw new InvalidOperationException("also-remove broke"));
        var log = new RecordingLogSink { OnPrint = clock.OnPrint };

        var outcome = ((RunResult.Done)FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves),
            workers: 1,
            log,
            DefaultMeshFiles,
            timeSinceStart: clock.Read)).Outcome;

        Assert.NotEmpty(healthy.RestingObjects.Rounds);
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, outcome.RestingObjects.Outcome);
        Assert.Empty(outcome.RestingObjects.Rounds);
        Assert.Equal([RoundKind.ChangedTerrain, RoundKind.TooClose, RoundKind.LeftBehind], outcome.Final.Rounds.Select(round => round.Kind));
        Assert.NotEmpty(outcome.Removals);
        Assert.Equal(outcome.Removals.Select(removal => removal.TargetIndex), outcome.Written.Removed.Select(id => id.Index));
        Assert.Equal(
            "  Unexpected error in the also-remove step (InvalidOperationException: also-remove broke). "
            + "The also-remove rounds did not complete, so objects resting on removed ones were not removed.",
            Assert.Single(UnexpectedErrorLines(log)));
        Assert.Equal("Also remove: not performed because of an unexpected error.", Assert.Single(SectionLines(log, "follow-up")));
        Assert.Same(ReportFileDetails.StoppedByEarlierFailure, outcome.RestingObjects.ReportFileDetails);
        Assert.Equal(StepOutcome.StoppedByEarlierFailure, outcome.RestingObjects.ReportFileDetails.Outcome);
        Assert.Equal(
            "Report-file details: not performed because Also remove was not performed.", Assert.Single(SectionLines(log, "explanationProblems")));
    }

    [Fact]
    public void AnAlsoRemoveFailureDuringItsRoundsShowsInTheNormalLogAndItsStackTraceInTheDetailedLog()
    {
        const string errorLine =
            "  Unexpected error in the also-remove step (InvalidOperationException: also-remove broke). "
            + "The also-remove rounds did not complete, so objects resting on removed ones were not removed.";

        var normal = RunWithFailingAlsoRemoveRounds(SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog);
        var detailed = RunWithFailingAlsoRemoveRounds(SettingsVariants.TouchWithLeftBehindAndMarkerMoves);

        Assert.Equal([errorLine], SectionLines(normal, "unexpectedErrors").ToList());
        Assert.Equal("Also remove: not performed because of an unexpected error.", Assert.Single(SectionLines(normal, "follow-up")));
        var detailedLines = SectionLines(detailed, "unexpectedErrors");
        Assert.Equal([errorLine, "    Stack trace:", "      System.InvalidOperationException: also-remove broke"], detailedLines.Take(3).ToList());
        Assert.Contains(detailedLines.Skip(3), line => line.StartsWith("         at ", StringComparison.Ordinal));
    }

    /// <remarks>Failing the clock reading at the end of the rounds fails them after they were decided.</remarks>
    private static RecordingLogSink RunWithFailingAlsoRemoveRounds(string variant)
    {
        var clock = new AlsoRemoveRoundsEndClock(() => throw new InvalidOperationException("also-remove broke"));
        var log = new RecordingLogSink { OnPrint = clock.OnPrint };
        var outcome = ((RunResult.Done)FixtureRun.Run(
            SettingsVariants.Of(variant),
            workers: 1,
            log,
            DefaultMeshFiles,
            timeSinceStart: clock.Read)).Outcome;
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, outcome.RestingObjects.Outcome);
        return log;
    }

    [Fact]
    public void ALeftBehindFailureLeavesTheDecisionsAsTheyWereAndTheRunCompletes()
    {
        var healthy = Run(new RecordingLogSink());
        FailingBaseFacts? bases = null;
        var log = new RecordingLogSink
        {
            OnPrint = section =>
            {
                if (bases is null) return;
                if (section.Id == "left-without-support-step") bases.Armed = true;
                if (section.Id == "leftovers") bases.Armed = false;
            },
        };

        var outcome = RunWithBases(log, inner => bases = new FailingBaseFacts(inner));

        Assert.Contains(healthy.LeftBehind.Evaluations, evaluation => evaluation.IsRemoved);
        Assert.DoesNotContain(outcome.LeftBehind.Evaluations, evaluation => evaluation.IsRemoved);
        Assert.Equal(outcome.Removals.Select(removal => removal.TargetIndex), outcome.Written.Removed.Select(id => id.Index));
        Assert.Equal(
            $"  Unexpected error in the step for invisible objects left behind (InvalidOperationException: {FailingBaseFacts.FailureMessage}). "
            + "That step did not run, so invisible objects whose surroundings were removed were not removed.",
            Assert.Single(UnexpectedErrorLines(log)));
        Assert.Contains("Invisible objects left behind: not performed because of an unexpected error.", SectionLines(log, "leftovers"));
        Assert.Contains("Moved kept markers: not performed because Invisible objects left behind was not performed.", SectionLines(log, "relocations"));
        Assert.Equal(StepOutcome.StoppedByEarlierFailure, outcome.MarkerMoves.Outcome);
    }

    [Fact]
    public void AFailureBuildingTheReportTablesIsReportedWhereTheWritersFailureWouldBeAndTheRunEndsNormally()
    {
        var log = new RecordingLogSink();

        var result = FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves),
            workers: 1,
            log,
            DefaultMeshFiles,
            wrapPlugin: null,
            wrapBases: inner => new BaseFactsFailingInReportTables(inner));

        Assert.IsType<RunResult.Done>(result);
        Assert.Equal(
            $"  Unexpected error in the report files (InvalidOperationException: {BaseFactsFailingInReportTables.FailureMessage}). "
            + "Some or all of the report files are missing; the patch is not affected.",
            Assert.Single(UnexpectedErrorLines(log)));
        Assert.Equal("Report files: not performed because of an unexpected error.", Assert.Single(SectionLines(log, "written")));
        Assert.Equal("done", log.Sections.Last().Id);
    }

    /// <remarks>
    /// The report-file details are computed after the clock reading at the end of the also-remove rounds, and the first
    /// base fact they read, a mesh origin's base, fails.
    /// </remarks>
    [Fact]
    public void AReportFileDetailsFailureIsReportedAfterThePatchIsWrittenWhereItWasBeforeAndTheRunCompletes()
    {
        FailingBaseFacts? bases = null;
        var clock = new AlsoRemoveRoundsEndClock(() => bases!.FailNextRead());
        var log = new RecordingLogSink { OnPrint = clock.OnPrint };

        var result = FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.Support),
            workers: 1,
            log,
            DefaultMeshFiles,
            wrapPlugin: null,
            wrapBases: inner => bases = new FailingBaseFacts(inner),
            timeSinceStart: clock.Read);

        var done = Assert.IsType<RunResult.Done>(result);
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, done.Outcome.RestingObjects.ReportFileDetails.Outcome);
        var errorLine =
            $"  Unexpected error in the report-file details (InvalidOperationException: {FailingBaseFacts.FailureMessage}). "
            + "The touching pairs or mesh origins that the report files list are missing from them.";
        Assert.Equal(errorLine, Assert.Single(UnexpectedErrorLines(log)));
        var write = log.Sections.FindIndex(section => section.Id == "write");
        var explanation = log.Sections.FindIndex(section => section.Id == "explanationProblems");
        Assert.True(write < explanation);
        Assert.Equal(["Report-file details: not performed because of an unexpected error."], log.Sections[explanation].Lines.ToList());
        Assert.Equal("meshProblems", log.Sections[explanation + 1].Id);
        Assert.Equal("archiveWarnings", log.Sections[explanation + 2].Id);
        Assert.Equal("unexpectedErrors", log.Sections[explanation + 3].Id);
        Assert.Equal(errorLine, Assert.Single(log.Sections[explanation + 3].Lines, IsErrorLine));
        Assert.Equal("done", log.Sections.Last().Id);
    }

    /// <remarks>Report words the failures the steps return in their results; it never adds them to the channel the steps record into.</remarks>
    [Fact]
    public void FailuresTheStepsReturnInTheirResultsAreNeverAddedToTheRunsErrorChannel()
    {
        var wholePart = CaptureChannelAfter(wrapPlugin: inner => new FailingHomeCellPlugin(inner), wrapBases: null);
        var reportTables = CaptureChannelAfter(wrapPlugin: null, wrapBases: inner => new BaseFactsFailingInReportTables(inner));

        Assert.Contains("marker moves", wholePart.Log.Sections.SelectMany(section => section.Lines).Single(line => line.Contains("Unexpected error in")));
        Assert.NotEmpty(wholePart.Channels);
        Assert.NotEmpty(reportTables.Channels);
        Assert.All(wholePart.Channels, channel => Assert.Empty(channel.Since(default)));
        Assert.Contains("report files", Assert.Single(UnexpectedErrorLines(reportTables.Log)));
        Assert.All(reportTables.Channels, channel => Assert.Empty(channel.Since(default)));
    }

    private static (RecordingLogSink Log, List<IUnexpectedErrors> Channels) CaptureChannelAfter(
        Func<IPluginRecords, IPluginRecords>? wrapPlugin, Func<IBaseFacts, IBaseFacts>? wrapBases)
    {
        var log = new RecordingLogSink();
        var channels = new List<IUnexpectedErrors>();

        FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves),
            workers: 1,
            log,
            (dataFolder, release, plugins) => new ChannelCapturingFactory(DefaultMeshFiles(dataFolder, release, plugins), channels),
            wrapPlugin,
            wrapBases);
        return (log, channels);
    }

    private sealed class ChannelCapturingFactory(IMeshFilesFactory inner, List<IUnexpectedErrors> channels) : IMeshFilesFactory
    {
        public IMeshFiles Open(ShapeInclusion inclusion, IUnexpectedErrors unexpectedErrors)
        {
            channels.Add(unexpectedErrors);
            return inner.Open(inclusion, unexpectedErrors);
        }
    }

    /// <summary>
    /// The run's clock, which calls the action at the reading that ends the also-remove rounds. The too-close section prints just
    /// before the also-remove step, which reads the clock when its rounds start and when they end.
    /// </summary>
    private sealed class AlsoRemoveRoundsEndClock(Action atRoundsEnd)
    {
        private const int RoundsEndReading = 2;
        private int? _readingsSinceTooClose;

        public void OnPrint(LogSection section)
        {
            if (section.Id == "tooClose") _readingsSinceTooClose = 0;
        }

        public TimeSpan Read()
        {
            if (_readingsSinceTooClose is { } readings)
            {
                _readingsSinceTooClose = readings + 1;
                if (_readingsSinceTooClose == RoundsEndReading) atRoundsEnd();
            }
            return TimeSpan.Zero;
        }
    }

    private static ImmutableArray<string> SectionLines(RecordingLogSink log, string id) =>
        [.. log.Sections.Where(section => section.Id == id).SelectMany(section => section.Lines)];

    private static RunOutcome RunWithBases(RecordingLogSink log, Func<IBaseFacts, IBaseFacts> wrapBases) =>
        ((RunResult.Done)FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves), workers: 1, log, DefaultMeshFiles, wrapPlugin: null, wrapBases)).Outcome;

    /// <summary>The error lines, without the stack traces the detailed log prints under them.</summary>
    private static ImmutableArray<string> UnexpectedErrorLines(RecordingLogSink log) => [.. SectionLines(log, "unexpectedErrors").Where(IsErrorLine)];

    private static bool IsErrorLine(string line) => line.StartsWith("  Unexpected error ", StringComparison.Ordinal);

    [Fact]
    public void ARunWithoutUnexpectedErrorsPrintsNoErrorLines()
    {
        var log = new RecordingLogSink();

        Run(log);

        Assert.All(log.Sections.Where(section => section.Id == "unexpectedErrors"), section => Assert.Empty(section.Lines));
    }
}
