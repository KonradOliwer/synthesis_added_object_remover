using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using static AddedObjectRemover.Tests.EndToEnd.RingWorld;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The also-remove follow-up of the visible left-behind rounds through a whole run on a small world of its own. A crate stands in the
/// middle with a door on it and a pad of a trusted mod on the door; six crates of the cleaned mod stand around the middle crate in six
/// directions, each too close to a pad of an untrusted mod. The six are removed for being too close, which leaves the middle crate with
/// its surroundings removed: the first visible round removes it. The door rests on the crate, which that round still counts as kept,
/// so the follow-up of that round removes the door; no undecided object's surroundings changed after it, so no second visible round
/// runs. The pad on the door is the other mod's object that goes with it.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class VisibleLeftBehindFollowUpRunTests
{
    public static TheoryData<FollowUpRemovalMode> Modes => new(Enum.GetValues<FollowUpRemovalMode>().Where(mode => mode != FollowUpRemovalMode.Nothing));

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheVisibleStepRemovesTheMiddleCrateAndKeepsTheDoorOnItBecauseTheCrateWasNotRemovedBeforeThatRound(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var removal = Assert.Single(outcome.Removals.OfType<ObjectWithReason.VisibleLeftBehind>());
        Assert.Equal(MiddleCrate, outcome.Collected.Targets[removal.TargetIndex].EditorId);
        var door = Assert.Single(outcome.VisibleLeftBehind.Evaluations, check => outcome.Collected.Targets[check.TargetIndex].EditorId == DoorEditorId);
        Assert.Equal(VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, door.Decision);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheFollowUpIsSeededByTheVisibleRoundAloneAndRemovesTheDoorThatRestsOnTheRemovedCrate(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var visibleRound = Assert.Single(outcome.Final.Rounds, round => round.Kind == RoundKind.VisibleLeftBehind);
        var followUp = outcome.VisibleLeftBehind.FollowUp;
        Assert.True(followUp.HadSeeds);
        Assert.Equal(new[] { visibleRound }, followUp.SeedRounds.ToArray());
        var doorId = IdOf(outcome, DoorEditorId);
        var removed = Assert.IsType<Decision.Removed>(outcome.Final.Of(doorId));
        Assert.Contains(removed.Round, followUp.Rounds);
        Assert.True(removed.Round.Number > visibleRound.Number);
        Assert.Equal(mode == FollowUpRemovalMode.EverythingTouchingIt, removed.Reason is RemovalReason.Touching);
        Assert.Equal(mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport, removed.Reason is RemovalReason.LostSupport);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheAlsoRemoveStepNeitherSeesTheVisibleRoundNorRemovesTheDoor(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var visibleRound = Assert.Single(outcome.Final.Rounds, round => round.Kind == RoundKind.VisibleLeftBehind);
        Assert.DoesNotContain(visibleRound, outcome.RestingObjects.SeedRounds);
        Assert.All(outcome.RestingObjects.Rounds, round => Assert.DoesNotContain(IdOf(outcome, DoorEditorId), outcome.Final.RemovedIn(round)));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AnotherModsObjectOnTheDoorTheFollowUpRemovedIsPulledDown(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var pulled = Assert.Single(outcome.OtherModsObjects.Objects);
        Assert.Equal(OtherModsObjectFate.PulledDown, pulled.Fate);
        Assert.Equal(FloorTop, pulled.NewPosition.Z);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheOtherModsObjectsAreCheckedOnceAfterTheFollowUpsWhichPrintBetweenThem(FollowUpRemovalMode mode)
    {
        var log = new RecordingLogSink();
        var followUpSection = mode == FollowUpRemovalMode.EverythingTouchingIt ? "touch-after-visible" : "anchoring-after-visible";

        FixtureRun.Run(CreateSettings(mode), workers: 1, log, DefaultMeshFiles, createLoadOrder: Create);

        var ids = log.Sections.Select(section => section.Id).ToList();
        Assert.True(ids.IndexOf("visible-leftovers") < ids.IndexOf(followUpSection));
        Assert.True(ids.IndexOf(followUpSection) < ids.IndexOf("left-without-support-step"));
        Assert.Single(ids, "left-without-support-step");
        Assert.Contains(
            log.Sections.Single(section => section.Id == followUpSection).Lines,
            line => line.StartsWith("Also remove after visible objects left behind: 2 rounds in ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheSummaryCountsTheFollowUpRemovalAndTheTotalsAddUp(FollowUpRemovalMode mode)
    {
        var log = new RecordingLogSink();

        var outcome = ((RunResult.Done)FixtureRun.Run(CreateSettings(mode), workers: 1, log, DefaultMeshFiles, createLoadOrder: Create)).Outcome;

        Assert.Equal(RingCrates + 2, outcome.Removals.Length);
        Assert.Equal(
            "Removed 8 objects (6 too close, 1 follow-up removals, 1 visible objects left behind, 0 invisible objects left behind, 0 linked to removed objects).",
            Assert.Single(log.Sections.Single(section => section.Id == "removal-summary").Lines));
    }

    [Fact]
    public void TheVisibleReportListsEveryEvaluatedObjectByRecordKeyAndTheSupportReportNumbersTheFollowUpAfterTheAlsoRemoveStep()
    {
        var run = PipelineRun.Execute(CreateSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport), workers: 1, Create);

        var visible = run.Reports.Single(report => report.Name == "leftover-visible-objects.csv").Lines;
        Assert.Equal(2, visible.Count - 1);
        Assert.Contains(",removed,surroundings removed", visible[1], StringComparison.Ordinal);
        Assert.Contains(MiddleCrate, visible[1], StringComparison.Ordinal);
        Assert.Contains(",kept,surroundings mostly kept", visible[2], StringComparison.Ordinal);
        Assert.Contains(DoorEditorId, visible[2], StringComparison.Ordinal);
        Assert.Contains("Visible objects left behind diagnostics: wrote 2 evaluations to <root>/Output/Reports/leftover-visible-objects.csv.", run.Log);
        var anchoring = run.Reports.Single(report => report.Name == "anchoring.csv").Lines;
        var doorRow = Assert.Single(anchoring, line => line.Contains(DoorEditorId, StringComparison.Ordinal));
        Assert.StartsWith("2,", doorRow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTouchReportsCoverTheAlsoRemoveStepOnly()
    {
        var run = PipelineRun.Execute(CreateSettings(FollowUpRemovalMode.EverythingTouchingIt), workers: 1, Create);

        Assert.DoesNotContain(run.Reports.Single(report => report.Name == "edges.csv").Lines, line => line.Contains(DoorEditorId, StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports.Single(report => report.Name == "components.csv").Lines, line => line.Contains(DoorEditorId, StringComparison.Ordinal));
    }

    /// <remarks>The run's clock fails at the reading that starts the first follow-up, after the also-remove step's section and the first visible round.</remarks>
    [Theory]
    [MemberData(nameof(Modes))]
    public void AFailedFollowUpIsReportedAndTheNextVisibleRoundRemovesTheDoor(FollowUpRemovalMode mode)
    {
        var clock = new FailingAfterSectionClock(mode == FollowUpRemovalMode.EverythingTouchingIt ? "touch" : "anchoring");
        var log = new RecordingLogSink { OnPrint = clock.OnPrint };

        var outcome = ((RunResult.Done)FixtureRun.Run(CreateSettings(mode), workers: 1, log, DefaultMeshFiles, timeSinceStart: clock.Read, createLoadOrder: Create)).Outcome;

        Assert.Equal(StepOutcome.StoppedByUnexpectedError, outcome.VisibleLeftBehind.FollowUp.Outcome);
        Assert.Equal(2, outcome.VisibleLeftBehind.Rounds.Length);
        var door = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, DoorEditorId)));
        Assert.Equal(outcome.VisibleLeftBehind.Rounds[1], door.Round);
        Assert.Equal(
            $"  Unexpected error in the also-remove follow-ups of the visible objects left behind (InvalidOperationException: {FailingAfterSectionClock.FailureMessage}). "
            + "The follow-ups stopped there: the removals of the passes before stand, and objects resting on what the later rounds removed were not removed.",
            Assert.Single(log.Sections.Where(section => section.Id == "unexpectedErrors").SelectMany(section => section.Lines), line => line.StartsWith("  Unexpected error ", StringComparison.Ordinal)));
        Assert.Equal(
            "Also remove after visible objects left behind: not performed because of an unexpected error.",
            Assert.Single(log.Sections.Single(section => section.Id == "follow-up-after-visible").Lines));
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, Mutagen.Bethesda.GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static Settings CreateSettings(FollowUpRemovalMode mode) => RingWorld.CreateSettings(mode);

    private static IReadOnlyList<ISkyrimMod> Create(string dataFolder) => RingWorld.Create(dataFolder, RingWorld.AddDoorWithPadOnIt);

    /// <summary>
    /// The run's clock, which throws once, at its third reading after the section with the given id was printed: the first visible
    /// round reads it twice, at its start and at its end, before the first follow-up starts.
    /// </summary>
    private sealed class FailingAfterSectionClock(string sectionId)
    {
        public const string FailureMessage = "follow-up broke";

        private const int VisibleRoundReadings = 2;

        private bool _armed;
        private int _readingsSinceArmed;

        public void OnPrint(LogSection section) => _armed |= section.Id == sectionId;

        public TimeSpan Read()
        {
            if (!_armed || ++_readingsSinceArmed <= VisibleRoundReadings) return TimeSpan.Zero;

            _armed = false;
            throw new InvalidOperationException(FailureMessage);
        }
    }
}
