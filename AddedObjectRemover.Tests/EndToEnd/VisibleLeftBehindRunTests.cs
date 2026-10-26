using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The visible left-behind step inside a whole run of the fixture world: where it stands in the order and how its removals are reported.</summary>
public class VisibleLeftBehindRunTests
{
    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static Settings SettingsWith(bool visibleOn)
    {
        var settings = SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves);
        settings.Standard.LeftoverVisibleObjects = new LeftoverVisibleObjectSettings
        {
            RemoveLeftoverVisibleObjects = visibleOn,
            SurroundingObjects = new VisibleSurroundingObjectsSettings
            {
                SearchRadius = 8192f,
                MinOccupiedDirections = 1,
                DirectionThresholdPercent = 10,
                ClearedDirectionsPercent = 10,
            },
        };
        return settings;
    }

    [Fact]
    public void SwitchedOffTheStepDoesNothingAndLeavesAnEmptyRoundBetweenAlsoRemoveAndLeftBehind()
    {
        var outcome = FixtureRun.Execute(SettingsWith(visibleOn: false), workers: 1);

        Assert.Equal(StepOutcome.SwitchedOff, outcome.VisibleLeftBehind.Outcome);
        var rounds = outcome.Final.Rounds;
        var visible = Assert.Single(rounds, round => round.Kind == RoundKind.VisibleLeftBehind);
        Assert.Empty(outcome.Final.RemovedIn(visible));
        Assert.Equal(rounds.Single(round => round.Kind == RoundKind.LeftBehind).Number, visible.Number + 1);
        Assert.DoesNotContain(outcome.Removals, removal => removal is VisibleLeftBehindRemoval);
    }

    [Fact]
    public void SwitchedOnTheStepPrintsAfterAlsoRemoveAndItsRemovalsAreCountedInTheSummary()
    {
        var log = new RecordingLogSink();

        var outcome = ((RunResult.Done)FixtureRun.Run(SettingsWith(visibleOn: true), workers: 1, log, DefaultMeshFiles)).Outcome;

        Assert.Equal(StepOutcome.Performed, outcome.VisibleLeftBehind.Outcome);
        var ids = log.Sections.Select(section => section.Id).ToList();
        Assert.True(ids.IndexOf("follow-up") < ids.IndexOf("visible-leftovers"));
        Assert.True(ids.IndexOf("visible-leftovers") < ids.IndexOf("leftovers"));
        var visibleRemovals = outcome.Removals.OfType<VisibleLeftBehindRemoval>().ToList();
        Assert.NotEmpty(visibleRemovals);
        Assert.Equal(outcome.VisibleLeftBehind.Evaluations.Count(evaluation => evaluation.IsRemoved), visibleRemovals.Count);
        var summary = Assert.Single(log.Sections.Single(section => section.Id == "removal-summary").Lines);
        Assert.Contains($", {visibleRemovals.Count} visible objects left behind, ", summary, StringComparison.Ordinal);
        Assert.Equal(outcome.Removals.Select(removal => removal.TargetIndex), outcome.Written.Removed.Select(id => id.Index));
    }
}
