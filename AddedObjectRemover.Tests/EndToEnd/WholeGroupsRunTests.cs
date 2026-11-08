namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A whole run on an interior with two groups of the cleaned mod's crates, one crate of the first group removed as too close: with
/// "remove whole groups" on, the rest of that group goes too, named by its seed and size in the log and the removals report, and the
/// second group stays; off, only the crate too close goes and nothing of the step prints.
/// </summary>
public sealed class WholeGroupsRunTests
{
    private const string Seed = "T_First0";

    [Fact]
    public void TheGroupOfATooCloseCrateIsRemovedWholeAndTheOtherGroupStays()
    {
        var (outcome, _) = Run(wholeGroups: true);

        Assert.Equal(["T_First1", "T_First2"], InGroupWithClash(outcome).Select(removal => EditorIdOf(outcome, removal.TargetIndex)).Order(StringComparer.Ordinal));
        Assert.All(InGroupWithClash(outcome), removal => Assert.Equal((Seed, RestoreWhereRemovedWorlds.GroupCrates), (EditorIdOf(outcome, removal.SeedTargetIndex), removal.GroupSize)));
        Assert.DoesNotContain(outcome.Removals, removal => EditorIdOf(outcome, removal.TargetIndex)!.StartsWith("T_Second", StringComparison.Ordinal));
        var group = Assert.Single(outcome.WholeGroups.Groups);
        Assert.Equal(Seed, EditorIdOf(outcome, group.Seed.Index));
    }

    [Fact]
    public void TheLogPrintsTheSettingTheStepLineAndTheGroupAndTheReportNamesTheSeed()
    {
        var (_, run) = Run(wholeGroups: true);

        Assert.Contains("Objects left behind, whole groups: on; gap 300 units.", run.Log);
        Assert.Contains(run.Log, line => line.StartsWith("Objects in groups with a clash removal: 2 removed in 1 group (largest 3)", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Group of 3: seed removed ", StringComparison.Ordinal) && line.Contains(Seed, StringComparison.Ordinal));
        var removals = Assert.Single(run.Reports, report => report.Name == ReportFileNames.RemovalsFileName);
        Assert.Equal(2, removals.Lines.Count(line => line.Contains("in a group with a clash removal (seed removed", StringComparison.Ordinal) && line.Contains("group of 3)", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRoundIsFollowedUpByAlsoRemove()
    {
        var (outcome, run) = Run(wholeGroups: true);

        Assert.True(outcome.WholeGroups.FollowUp.HadSeeds);
        Assert.Contains(run.Log, line => line.StartsWith("Also remove after objects in groups with a clash removal", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRestorePutsBackTheDisabledRockOnlyWhenTheWholeGroupGoes()
    {
        Assert.True(Rock(RunWithRestore(wholeGroups: true)).IsRestored);
        Assert.False(Rock(RunWithRestore(wholeGroups: false)).IsRestored);
    }

    [Fact]
    public void SwitchedOffOnlyTheTooCloseCrateGoesAndTheStepPrintsNothing()
    {
        var (outcome, run) = Run(wholeGroups: false);

        Assert.Equal(StepOutcome.SwitchedOff, outcome.WholeGroups.Outcome);
        Assert.Equal([Seed], outcome.Removals.Select(removal => EditorIdOf(outcome, removal.TargetIndex)));
        Assert.DoesNotContain(run.Log, line => line.Contains("whole groups", StringComparison.Ordinal) || line.Contains("groups with a clash removal", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOutputDoesNotDependOnTheNumberOfWorkers()
    {
        var settings = SettingsOf(wholeGroups: true);

        Assert.Equal(
            RestoreWhereRemovedRunTests.Run(settings, RestoreWhereRemovedWorlds.CreateTwoCrateGroups, workers: 1).Run.ToExpectedOutputLines(),
            RestoreWhereRemovedRunTests.Run(settings, RestoreWhereRemovedWorlds.CreateTwoCrateGroups, workers: 4).Run.ToExpectedOutputLines());
    }

    private static (RunOutcome Outcome, RunOutput Run) Run(bool wholeGroups) =>
        RestoreWhereRemovedRunTests.Run(SettingsOf(wholeGroups), RestoreWhereRemovedWorlds.CreateTwoCrateGroups);

    /// <summary>The restore at the settings of the restore runs: the interior is cleared only when every crate is removed.</summary>
    private static RunOutcome RunWithRestore(bool wholeGroups)
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf();
        settings.Standard.LeftoverObjects.RemoveWholeGroups = wholeGroups;
        settings.Standard.LeftoverObjects.RemoveObjectsInClearedAreas = false;
        return RestoreWhereRemovedRunTests.Run(settings, RestoreWhereRemovedWorlds.CreateCrateGroupWithADisabledRock).Outcome;
    }

    private static PlacedChangeJudged Rock(RunOutcome outcome) => Assert.Single(outcome.RestoreWhereRemoved.Objects, change => change.Change.EditorId == "B_Rock");

    /// <summary>The too-close settings of the restore runs, nothing restored, and the step for whole groups at its default gap.</summary>
    private static Settings SettingsOf(bool wholeGroups)
    {
        var settings = RestoreWhereRemovedRunTests.SettingsOf(RestoreWhereRemovedRunTests.SwitchEverythingOff);
        settings.Standard.LeftoverObjects.RemoveWholeGroups = wholeGroups;
        settings.Standard.LeftoverObjects.RemoveObjectsInClearedAreas = false;
        settings.Standard.LeftoverInvisibleObjects.RemoveLeftoverInvisibleObjects = false;
        return settings;
    }

    private static List<ObjectWithReason.InGroupWithClash> InGroupWithClash(RunOutcome outcome) => [.. outcome.Removals.OfType<ObjectWithReason.InGroupWithClash>()];

    private static string? EditorIdOf(RunOutcome outcome, int targetIndex) => outcome.Collected.Targets[targetIndex].EditorId;
}
