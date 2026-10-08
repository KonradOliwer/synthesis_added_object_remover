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
        Assert.Equal(outcome.Removals.Length, outcome.Written.Removed);
        var errorSection = Assert.Single(log.Sections, section => section.Id == "unexpectedErrors" && section.Lines.Length > 0);
        Assert.Equal(
            $"  Unexpected error in the marker moves (InvalidOperationException: {FailingHomeCellPlugin.FailureMessage}). "
            + "No kept marker was moved, so markers inside other mods' objects stay where they are.",
            Assert.Single(errorSection.Lines));
    }

    [Fact]
    public void AnAlsoRemoveFailureAfterItsRoundsWereAppliedDropsThemAndTheLaterStepsStillRun()
    {
        var healthy = Run(new RecordingLogSink());
        var log = new RecordingLogSink { FailAfterMeasuredWork = new InvalidOperationException("also-remove broke") };

        var outcome = Run(log);

        Assert.NotEmpty(healthy.RestingObjects.Rounds);
        Assert.Empty(outcome.RestingObjects.Rounds);
        Assert.NotEmpty(outcome.Removals);
        Assert.Equal(outcome.Removals.Length, outcome.Written.Removed);
        Assert.Equal(
            "  Unexpected error in the also-remove step (InvalidOperationException: also-remove broke). "
            + "The also-remove rounds did not complete, so objects resting on removed ones were not removed.",
            Assert.Single(UnexpectedErrorLines(log)));
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
                if (section.Id is "touch" or "follow-up") bases.Armed = true;
                if (section.Id == "leftovers") bases.Armed = false;
            },
        };

        var outcome = RunWithBases(log, inner => bases = new FailingBaseFacts(inner));

        Assert.True(healthy.LeftBehind.RemovedCount > 0);
        Assert.Equal(0, outcome.LeftBehind.RemovedCount);
        Assert.Equal(outcome.Removals.Length, outcome.Written.Removed);
        Assert.Equal(
            $"  Unexpected error in the left-behind step (InvalidOperationException: {FailingBaseFacts.FailureMessage}). "
            + "The left-behind step did not run, so invisible objects whose surroundings were removed were not removed.",
            Assert.Single(UnexpectedErrorLines(log)));
    }

    private static RunOutcome RunWithBases(RecordingLogSink log, Func<IBaseFacts, IBaseFacts> wrapBases) =>
        ((RunResult.Done)FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves), workers: 1, log, DefaultMeshFiles, wrapPlugin: null, wrapBases)).Outcome;

    private static ImmutableArray<string> UnexpectedErrorLines(RecordingLogSink log) =>
        [.. log.Sections.Where(section => section.Id == "unexpectedErrors").SelectMany(section => section.Lines)];

    [Fact]
    public void ARunWithoutUnexpectedErrorsPrintsNoErrorLines()
    {
        var log = new RecordingLogSink();

        Run(log);

        Assert.All(log.Sections.Where(section => section.Id == "unexpectedErrors"), section => Assert.Empty(section.Lines));
    }
}
