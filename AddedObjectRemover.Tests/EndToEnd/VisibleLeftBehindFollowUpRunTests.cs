using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The second also-remove pass through a whole run on a small world of its own. A crate stands in the middle with a door on it and a
/// pad of a trusted mod on the door; six crates of the cleaned mod stand around the middle crate in six directions, each too close to
/// a pad of an untrusted mod. The six are removed for being too close, which leaves the middle crate with its surroundings removed:
/// the visible step removes it. The door rests on the crate, which that step still counts as kept, so the door stays for the second
/// pass to remove. The pad on the door is the other mod's object that goes with it.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class VisibleLeftBehindFollowUpRunTests
{
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string TrustedPlugin = "Trusted.esp";
    private const string UntrustedPlugin = "Untrusted.esp";
    private const string MiddleCrate = "T_MiddleCrate";
    private const string Door = "T_DoorOnMiddleCrate";
    private const string Pad = "Trusted_PadOnDoor";
    private const float RingDistance = 200f;
    private const float CauseShift = 45f;
    private const float FloorTop = 0f;
    private const int RingCrates = 6;
    private const int FirstRingAngle = 45;
    private const int AngleStep = 45;

    public static TheoryData<FollowUpRemovalMode> Modes => new(Enum.GetValues<FollowUpRemovalMode>().Where(mode => mode != FollowUpRemovalMode.Nothing));

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheVisibleStepRemovesTheMiddleCrateAndKeepsTheDoorOnItBecauseTheCrateWasNotRemovedBeforeThatRound(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var removal = Assert.Single(outcome.Removals.OfType<ObjectWithReason.VisibleLeftBehind>());
        Assert.Equal(MiddleCrate, outcome.Collected.Targets[removal.TargetIndex].EditorId);
        var door = Assert.Single(outcome.VisibleLeftBehind.Evaluations, check => outcome.Collected.Targets[check.TargetIndex].EditorId == Door);
        Assert.Equal(VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, door.Decision);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheSecondPassIsSeededByTheVisibleRoundAloneAndRemovesTheDoorThatRestsOnTheRemovedCrate(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var visibleRound = Assert.Single(outcome.Final.Rounds, round => round.Kind == RoundKind.VisibleLeftBehind);
        var second = outcome.RestingObjectsAfterVisibleLeftBehind;
        Assert.True(second.HadSeeds);
        Assert.Equal(new[] { visibleRound }, second.SeedRounds.ToArray());
        var doorId = IdOf(outcome, Door);
        var removed = Assert.IsType<Decision.Removed>(outcome.Final.Of(doorId));
        Assert.Contains(removed.Round, second.Rounds);
        Assert.True(removed.Round.Number > visibleRound.Number);
        Assert.Equal(mode == FollowUpRemovalMode.EverythingTouchingIt, removed.Reason is RemovalReason.Touching);
        Assert.Equal(mode == FollowUpRemovalMode.OnlyWhatLosesItsSupport, removed.Reason is RemovalReason.LostSupport);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheFirstPassNeitherSeesTheVisibleRoundNorRemovesTheDoor(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var visibleRound = Assert.Single(outcome.Final.Rounds, round => round.Kind == RoundKind.VisibleLeftBehind);
        Assert.DoesNotContain(visibleRound, outcome.RestingObjects.SeedRounds);
        Assert.All(outcome.RestingObjects.Rounds, round => Assert.DoesNotContain(IdOf(outcome, Door), outcome.Final.RemovedIn(round)));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void AnotherModsObjectOnTheDoorTheSecondPassRemovedIsPulledDown(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode), workers: 1, Create);

        var pulled = Assert.Single(outcome.OtherModsObjects.Objects);
        Assert.Equal(OtherModsObjectFate.PulledDown, pulled.Fate);
        Assert.Equal(FloorTop, pulled.NewPosition.Z);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheOtherModsObjectsAreCheckedOnceAfterBothPassesAndTheSecondPassPrintsBetweenThem(FollowUpRemovalMode mode)
    {
        var log = new RecordingLogSink();
        var secondPassSection = mode == FollowUpRemovalMode.EverythingTouchingIt ? "touch-after-visible" : "anchoring-after-visible";

        FixtureRun.Run(CreateSettings(mode), workers: 1, log, DefaultMeshFiles, createLoadOrder: Create);

        var ids = log.Sections.Select(section => section.Id).ToList();
        Assert.True(ids.IndexOf("visible-leftovers") < ids.IndexOf(secondPassSection));
        Assert.True(ids.IndexOf(secondPassSection) < ids.IndexOf("left-without-support-step"));
        Assert.Single(ids, "left-without-support-step");
        Assert.Contains(
            log.Sections.Single(section => section.Id == secondPassSection).Lines,
            line => line.StartsWith("Also remove after visible objects left behind: 2 rounds in ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void TheSummaryCountsTheSecondPassRemovalAsAFollowUpAndTheTotalsAddUp(FollowUpRemovalMode mode)
    {
        var log = new RecordingLogSink();

        var outcome = ((RunResult.Done)FixtureRun.Run(CreateSettings(mode), workers: 1, log, DefaultMeshFiles, createLoadOrder: Create)).Outcome;

        Assert.Equal(RingCrates + 2, outcome.Removals.Length);
        Assert.Equal(
            "Removed 8 objects (6 too close, 1 follow-up removals, 1 visible objects left behind, 0 invisible objects left behind, 0 linked to removed objects).",
            Assert.Single(log.Sections.Single(section => section.Id == "removal-summary").Lines));
    }

    [Fact]
    public void TheVisibleReportListsEveryEvaluatedObjectByRecordKeyAndTheSupportReportNumbersTheSecondPassAfterTheFirst()
    {
        var run = PipelineRun.Execute(CreateSettings(FollowUpRemovalMode.OnlyWhatLosesItsSupport), workers: 1, Create);

        var visible = run.Reports.Single(report => report.Name == "leftover-visible-objects.csv").Lines;
        Assert.Equal(2, visible.Count - 1);
        Assert.Contains(",removed,surroundings removed", visible[1], StringComparison.Ordinal);
        Assert.Contains(MiddleCrate, visible[1], StringComparison.Ordinal);
        Assert.Contains(",kept,surroundings mostly kept", visible[2], StringComparison.Ordinal);
        Assert.Contains(Door, visible[2], StringComparison.Ordinal);
        Assert.Contains("Visible objects left behind diagnostics: wrote 2 evaluations to <root>/Output/Reports/leftover-visible-objects.csv.", run.Log);
        var anchoring = run.Reports.Single(report => report.Name == "anchoring.csv").Lines;
        var doorRow = Assert.Single(anchoring, line => line.Contains(Door, StringComparison.Ordinal));
        Assert.StartsWith("2,", doorRow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTouchReportsCoverTheFirstPassOnly()
    {
        var run = PipelineRun.Execute(CreateSettings(FollowUpRemovalMode.EverythingTouchingIt), workers: 1, Create);

        Assert.DoesNotContain(run.Reports.Single(report => report.Name == "edges.csv").Lines, line => line.Contains(Door, StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports.Single(report => report.Name == "components.csv").Lines, line => line.Contains(Door, StringComparison.Ordinal));
    }

    private static TargetId IdOf(RunOutcome outcome, string editorId) => outcome.Collected.Targets.Single(target => target.EditorId == editorId).Id;

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, Mutagen.Bethesda.GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static Settings CreateSettings(FollowUpRemovalMode mode) => new()
    {
        DetailedLogAndReports = true,
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = false },
            },
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            Trust = new TrustSettings
            {
                TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension(TrustedPlugin)],
                NeverTrustedMods = [ModKey.FromNameAndExtension(UntrustedPlugin)],
                TrustModsRequiringCleanedModAndTheirMasters = false,
                TrustModsLoadingBeforeCleanedMod = false,
            },
            ShrinkOrTurn = SettingsVariants.NothingShrunkOrTurned,
            FollowUpRemoval = new FollowUpRemovalSettings { Mode = mode },
        },
    };

    /// <summary>The world, in load order: Skyrim.esm, Target.esp, Trusted.esp, Untrusted.esp.</summary>
    private static IReadOnlyList<ISkyrimMod> Create(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        var target = Create(spaces, FixtureWorld.TargetName, baseGame);
        var trusted = Create(spaces, TrustedPlugin, baseGame);
        var untrusted = Create(spaces, UntrustedPlugin, baseGame);

        baseGame.Interior.Place("Sky_Floor", baseGame.AddModeledStatic("Sky_FloorBase", FixtureMeshes.BuildingModel), new Vector3(0f, 0f, FloorTop - FixtureMeshes.BuildingHeight));
        var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var cause = untrusted.AddModeledStatic("U_CauseBase", FixtureMeshes.PadModel);
        target.Interior.Place(MiddleCrate, crate, Vector3.Zero);
        target.Interior.Place(Door, target.AddModeledDoor("T_DoorBase", FixtureMeshes.CrateModel), new Vector3(0f, 0f, FixtureMeshes.CrateSize));
        for (var ring = 0; ring < RingCrates; ring++)
        {
            var angle = (FirstRingAngle + ring * AngleStep) * MathF.PI / 180f;
            var position = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * RingDistance;
            target.Interior.Place($"T_Ring{ring}", crate, position);
            untrusted.Interior.Place($"U_Cause{ring}", cause, position + new Vector3(CauseShift, 0f, 0f));
        }
        trusted.Interior.Place(Pad, trusted.AddModeledStatic("Trusted_PadBase", FixtureMeshes.PadModel), new Vector3(0f, 0f, 2 * FixtureMeshes.CrateSize));
        return [baseGame.Mod, target.Mod, trusted.Mod, untrusted.Mod];
    }

    private static FixtureMod Create(FixtureSpaces spaces, string plugin, FixtureMod baseGame)
    {
        var mod = new FixtureMod(plugin, baseGame);
        spaces.AddOverridesTo(mod);
        return mod;
    }
}
