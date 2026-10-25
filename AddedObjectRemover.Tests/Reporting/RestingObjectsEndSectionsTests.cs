using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The also-remove, left-behind, marker-move and closing log sections: what each log mode shows, where the
/// mesh problems stay out of them, and that the detailed log reads exactly like its expected output.
/// </summary>
public sealed class RestingObjectsEndSectionsTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedAlsoRemove = 4;
    private const int LinkedPartner = 2;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly PluginName Target = new("Target.esp");
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    private static readonly TriangleStoreStats Meshes = new(Built: 3, TooLarge: 2, Triangles: 900);
    private static readonly PairTestStats Pairs = new(PairsTested: 10, TouchingPairs: 4, PairsWithoutGeometry: 1, TrianglePairsTested: 77);
    private static readonly TouchChainStatistics TouchStatistics = new(Components: 1, ComponentsWithFollowUpRemovals: 1, LargestComponent: 3, Levels: 1, MaxDepth: 1, Pairs);
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    [Theory]
    [InlineData(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, 4, 1)]
    [InlineData(SettingsVariants.Support, 1, 0)]
    public void AlsoRemoveCounts_MatchTheExpectedOutputLogs(string variant, int removed, int held)
    {
        var (world, alsoRemove, decisions) = RunFixture(SettingsVariants.Of(variant));
        var lines = LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, default, Detailed).Lines.ToList();

        var summary = lines.Single(line => line.StartsWith("Touching objects: ", StringComparison.Ordinal) || line.StartsWith("Anchoring: ", StringComparison.Ordinal));
        Assert.Contains($": {removed} removed ", summary);
        Assert.EndsWith($" {held} kept as referenced.", summary);
    }

    [Fact]
    public void Touch_CountsOnlyTouchingRemovalsAndHeldObjects()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var summary = LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Touching objects: 1 removed in 1 components", summary);
        Assert.EndsWith("; 1 kept as referenced.", summary);
    }

    [Fact]
    public void AlsoRemove_WithoutRoundsIsTheEmptySection()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var section = LogSections.BuildAlsoRemoveSection(world, alsoRemove with { HadSeeds = false, Rounds = [] }, decisions, Meshes, Detailed);

        Assert.Equal(LogSections.BuildNoFollowUpSection().Id, section.Id);
        Assert.Empty(section.Lines);
    }

    [Fact]
    public void AlsoRemove_AStoppedStepPrintsTheNotPerformedLine()
    {
        var (world, alsoRemove, decisions) = TouchScene();
        var stopped = alsoRemove with { Outcome = StepOutcome.StoppedByUnexpectedError, HadSeeds = false, Rounds = [], Failure = Broke(RestingObjectsParts.AlsoRemove) };

        var section = LogSections.BuildAlsoRemoveSection(world, stopped, decisions, Meshes, Detailed);

        Assert.Equal(LogSections.BuildNoFollowUpSection().Id, section.Id);
        Assert.Equal(["Also remove: not performed because of an unexpected error."], section.Lines.ToList());
    }

    [Fact]
    public void AlsoRemove_ASwitchedOffStepPrintsNothing()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var section = LogSections.BuildAlsoRemoveSection(
            world, alsoRemove with { Outcome = StepOutcome.SwitchedOff, HadSeeds = false, Rounds = [] }, decisions, Meshes, Detailed);

        Assert.Empty(section.Lines);
    }

    [Fact]
    public void AlsoRemove_PrintsTheSectionOfTheModeWithTheResultsChainsAndTime()
    {
        var (touchWorld, touch, touchDecisions) = TouchScene();
        var (supportWorld, support, supportDecisions) = SupportScene();

        var touchSection = LogSections.BuildAlsoRemoveSection(touchWorld, touch, touchDecisions, Meshes, Detailed);
        var supportSection = LogSections.BuildAlsoRemoveSection(supportWorld, support, supportDecisions, Meshes, Detailed);

        var expectedTouch = LogSections.BuildModeSection(touchWorld, touch, touchDecisions, Elapsed, Meshes, Detailed);
        var expectedSupport = LogSections.BuildModeSection(supportWorld, support, supportDecisions, Elapsed, Meshes, Detailed);
        Assert.Equal(expectedTouch.Id, touchSection.Id);
        Assert.Equal(expectedTouch.Lines.ToList(), touchSection.Lines.ToList());
        Assert.Equal(expectedSupport.Id, supportSection.Id);
        Assert.Equal(expectedSupport.Lines.ToList(), supportSection.Lines.ToList());
    }

    [Fact]
    public void Touch_NormalLogShowsKeptOnly()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var lines = LogSections.BuildModeSection(world, ChainSearchFailed(alsoRemove), decisions, Elapsed, Meshes, Normal).Lines.ToList();

        Assert.Equal(
            [
                $"  Kept {RecordNames.Describe(world.Targets[ProtectedAlsoRemove])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail} (touches removed {RecordNames.Describe(world.Targets[0])}).",
            ],
            lines);
    }

    [Fact]
    public void Touch_DetailedLogLeavesOutTheChainStatisticsWhenTheChainsCouldNotBeFound()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var lines = LogSections.BuildModeSection(world, ChainSearchFailed(alsoRemove), decisions, Elapsed, Meshes, Detailed).Lines.ToList();

        Assert.DoesNotContain(lines, line => line.StartsWith("Touching objects: ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("  Pairs: ", StringComparison.Ordinal));
        Assert.Equal("Also remove: 1 rounds in 1.5s.", lines[^1]);
    }

    [Fact]
    public void Touch_DetailedLogWithoutChainsAndWithoutAFailureOfTheirSearchIsABug()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        Assert.Throws<InvalidOperationException>(
            () => LogSections.BuildModeSection(world, alsoRemove with { TouchChains = null }, decisions, Elapsed, Meshes, Detailed));
    }

    private static RestingObjectsResult ChainSearchFailed(RestingObjectsResult alsoRemove) =>
        alsoRemove with { TouchChains = null, TouchChainsFailure = Broke(RestingObjectsParts.TouchChains) };

    [Fact]
    public void Touch_DetailedLogPutsTheStatisticsAfterKeptAndEndsWithTheAlsoRemoveTime()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        var lines = LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Detailed).Lines.ToList();

        Assert.StartsWith("  Kept ", lines[0]);
        Assert.StartsWith("Touching objects: ", lines[1]);
        Assert.Equal("Also remove: 1 rounds in 1.5s.", lines[^1]);
    }

    [Fact]
    public void Touch_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, alsoRemove, decisions) = TouchScene();

        AssertExpectedOutput(LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Detailed).Lines.ToList(), "Touch_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Support_CountsOnlyLostSupportRemovalsAndHeldObjects()
    {
        var (world, alsoRemove, decisions) = SupportScene();

        var summary = LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Anchoring: 1 removed over 1 iterations; 3 touching objects evaluated (4 evaluations), 1 kept without any contact points, 1 kept as referenced.", summary);
    }

    [Fact]
    public void Support_NormalLogShowsKeptOnly()
    {
        var (world, alsoRemove, decisions) = SupportScene();

        var lines = LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Normal).Lines.ToList();

        Assert.StartsWith("  Kept ", Assert.Single(lines));
    }

    [Fact]
    public void Anchoring_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, alsoRemove, decisions) = SupportScene();

        AssertExpectedOutput(LogSections.BuildModeSection(world, alsoRemove, decisions, Elapsed, Meshes, Detailed).Lines.ToList(), "Anchoring_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Anchoring_DetailedLogDescribesACandidateWithContactPointsButNoSupportWeight()
    {
        var (world, alsoRemove, decisions) = SupportScene();
        var diagnostic = new ZeroWeightDiagnostic(
            1, "m.nif", 2, 0.75f, 1.25f, new Box(Vector3.Zero, new Vector3(3, 4, 0)), 3,
            new SampleWeightCounts(1, 1, 1), double.NaN, 0.5, [new SupporterSamples(Supporter.Terrain, 2, float.NaN)]);
        var withCase = alsoRemove with { Evidence = [new SupportRoundDetails([], Pairs, [new ZeroWeightCase(5, diagnostic)])] };

        var detailed = LogSections.BuildModeSection(world, withCase, decisions, Elapsed, Meshes, Detailed).Lines.ToList();
        var normal = LogSections.BuildModeSection(world, withCase, decisions, Elapsed, Meshes, Normal).Lines.ToList();

        var line = Assert.Single(detailed, text => text.StartsWith("  Anchoring found no support weight for ", StringComparison.Ordinal));
        Assert.Contains("(mesh m.nif, iteration 1, 5 candidates evaluated before it in this run)", line);
        Assert.EndsWith(
            "2 contact points; weights computed with mesh size value 0.75; mesh size value 1.25 recomputed from bounds (0.0, 0.0, 0.0) to (3.0, 4.0, 0.0); 3 samples: 1 with weight above 0, 1 at 0, 1 not finite; "
            + "weights add up to NaN (0.5 for the touching samples); supporters: terrain 2 touching samples, raw weight NaN.",
            line);
        Assert.DoesNotContain(normal, text => text.Contains("no support weight", StringComparison.Ordinal));
    }

    [Fact]
    public void Leftovers_NormalLogShowsTheSummaryWithoutTimingThenKept()
    {
        var (world, leftBehind) = LeftBehindScene();

        var lines = LogSections.BuildLeftBehindSection(world, leftBehind, [new KeptObject(Protected, QuestReason, null)], Elapsed, Normal).Lines.ToList();

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("Invisible objects left behind: 2 evaluated; removed 1 (", lines[0]);
        Assert.DoesNotContain(" in 1.5s", lines[0]);
        Assert.StartsWith("  Kept ", lines[1]);
    }

    [Fact]
    public void Leftovers_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, leftBehind) = LeftBehindScene();
        KeptObject[] kept = [new(Protected, QuestReason, null)];


        AssertExpectedOutput(LogSections.BuildLeftBehindSection(world, leftBehind, kept, Elapsed, Detailed).Lines.ToList(), "Leftovers_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Leftovers_ASwitchedOffStepPrintsNothing()
    {
        var (world, _) = LeftBehindScene();

        var section = LogSections.BuildLeftBehindSection(world, LeftBehindResult.SwitchedOff, [], Elapsed, Detailed);

        Assert.Empty(section.Lines);
    }

    [Fact]
    public void Leftovers_AStoppedStepPrintsTheNotPerformedLine()
    {
        var (world, _) = LeftBehindScene();

        var lines = LogSections.BuildLeftBehindSection(
            world, new LeftBehindResult(StepOutcome.StoppedByUnexpectedError, [], [], Broke(LeftBehindParts.LeftBehind)), [], Elapsed, Normal).Lines.ToList();

        Assert.Equal("Invisible objects left behind: not performed because of an unexpected error.", Assert.Single(lines));
    }

    [Fact]
    public void Relocations_NormalLogKeepsWarningsAndLeftInPlaceLinesAndSummaryOnly()
    {
        var (world, markerMoves) = MarkerMovesScene();

        var lines = LogSections.BuildKeptMarkerMovesSection(world, markerMoves, Normal).Lines.ToList();

        Assert.Equal(3, lines.Count);
        Assert.StartsWith("  Warning: ", lines[0]);
        Assert.StartsWith("  Left kept ", lines[1]);
        Assert.Equal("Moved 2 kept markers out of other mods' objects; 1 left in place.", lines[2]);
    }

    [Fact]
    public void Relocations_ASwitchedOffStepPrintsNothing()
    {
        var (world, _) = MarkerMovesScene();

        var section = LogSections.BuildKeptMarkerMovesSection(world, MarkerMoves.SwitchedOff, Detailed);

        Assert.Empty(section.Lines);
    }

    [Fact]
    public void Relocations_AStoppedStepPrintsTheNotPerformedLine()
    {
        var (world, _) = MarkerMovesScene();

        var lines = LogSections.BuildKeptMarkerMovesSection(
            world, new MarkerMoves(StepOutcome.StoppedByUnexpectedError, [], [], [], MarkerMoveLimits.MaxDistance, Broke(MarkerMoveParts.MarkerMoves)), Normal).Lines.ToList();

        Assert.Equal("Moved kept markers: not performed because of an unexpected error.", Assert.Single(lines));
    }

    [Fact]
    public void Relocations_AStepStoppedByAnEarlierFailurePrintsWhichSectionWasNotPerformed()
    {
        var (world, _) = MarkerMovesScene();

        var lines = LogSections.BuildKeptMarkerMovesSection(
            world, MarkerMoves.NotPerformedAfterLeftBehindStopped, Normal).Lines.ToList();

        Assert.Equal("Moved kept markers: not performed because Invisible objects left behind was not performed.", Assert.Single(lines));
    }

    [Fact]
    public void Relocations_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, markerMoves) = MarkerMovesScene();

        AssertExpectedOutput(LogSections.BuildKeptMarkerMovesSection(world, markerMoves, Detailed).Lines.ToList(), "Relocations_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Relocations_TheLeftInPlaceLineNamesTheDistanceTheResultSearchedWithin()
    {
        var (world, markerMoves) = MarkerMovesScene();

        var lines = LogSections.BuildKeptMarkerMovesSection(world, markerMoves with { MaxDistance = 123 }, Normal).Lines.ToList();

        Assert.Contains("within 123 units.", lines[1]);
    }

    [Fact]
    public void Write_ShowsTheTimeOnlyInTheDetailedLogAndTheEnableParentLineAlways()
    {
        var written = new WriteSummary(
            Removed: [.. Enumerable.Range(0, 5).Select(index => new TargetId(index))], Moved: 2, EnableParentsReplaced: 3, Fitted: 0, OtherModsObjectsRemoved: 0, OtherModsObjectsPulledDown: 0);

        Assert.Equal(
            [
                "Wrote 7 overrides (5 removed, 2 kept markers moved).",
                "  3 removed objects had an Enable Parent; it was replaced by the player with \"opposite of parent\" so they stay disabled.",
            ],
            LogSections.BuildWriteSection(written, Elapsed, Normal).Lines.ToList());
        AssertExpectedOutput(LogSections.BuildWriteSection(written, Elapsed, Detailed).Lines.ToList(), "Write_ShowsTheTimeOnlyInTheDetailedLogAndTheEnableParentLineAlways");
    }

    [Fact]
    public void Write_OmitsTheEnableParentLineWhenNoneWasReplaced()
    {
        Assert.Single(LogSections.BuildWriteSection(new WriteSummary([new TargetId(0)], 0, 0, 0, 0, 0), Elapsed, Normal).Lines.ToList());
    }

    [Fact]
    public void Write_NamesTheOtherModsObjectsPulledDownAndRemovedOnlyWhenThereAreAny()
    {
        var written = new WriteSummary([new TargetId(0)], Moved: 0, EnableParentsReplaced: 0, Fitted: 1, OtherModsObjectsRemoved: 2, OtherModsObjectsPulledDown: 3);

        Assert.Equal(
            "Wrote 7 overrides (1 removed, 1 shrunk or turned to fit; other mods' objects: 3 pulled down, 2 removed).",
            LogSections.BuildWriteSection(written, Elapsed, Normal).Lines[0]);
    }

    [Fact]
    public void Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput()
    {
        var (world, decisions) = RunRounds();
        var removals = RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestLeftBehind.PerformedWithoutFindings);
        var detailed = Detailed with { Bases = null! };

        Assert.Empty(LogSections.BuildRemovalsSection(world, removals, Normal).Lines.ToList());
        Assert.Empty(LogSections.BuildSpacesSection(world, removals, Target, Normal).Lines.ToList());
        AssertExpectedOutput(LogSections.BuildRemovalsSection(world, removals, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput", "removals");
        AssertExpectedOutput(LogSections.BuildSpacesSection(world, removals, Target, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput", "spaces");
    }

    [Fact]
    public void Bounds_NormalLogShowsOnlyNonEmptyMeshFailuresWithoutIndent()
    {
        var failures = Statistics([new(Category(MeshReadError.NotFound), 2), new(Category(MeshReadError.NoRootNode), 1)]);

        Assert.Equal(["Mesh failures: 2 not found, 1 no root node."], LogSections.BuildBoundsSection(failures, TriangleFailureCounts.None, Normal).Lines.ToList());
        Assert.Empty(LogSections.BuildBoundsSection(Statistics([]), TriangleFailureCounts.None, Normal).Lines.ToList());
    }

    [Fact]
    public void Bounds_BothLogsShowTheMeshTriangleFailuresAndThoseAfterAGoodReadNextToTheMeshFailures()
    {
        var failures = Statistics([new(Category(MeshReadError.NotFound), 2)]);
        var triangles = new TriangleFailureCounts([new(Category(MeshReadError.UnexpectedException), 1), new(Category(MeshReadError.NotFound), 3)], AfterGoodRead: 1);

        Assert.Equal(
            [
                "Mesh failures: 2 not found.",
                "Mesh triangle failures: 3 not found, 1 unexpected error.",
                "Meshes whose triangles failed after reading fine earlier in the run: 1.",
            ],
            LogSections.BuildBoundsSection(failures, triangles, Normal).Lines.ToList());
        Assert.Equal(
            ["  Mesh triangle failures: 3 not found, 1 unexpected error.", "  Meshes whose triangles failed after reading fine earlier in the run: 1."],
            LogSections.BuildBoundsSection(Statistics([]), triangles, Detailed).Lines.Skip(1).ToList());
    }

    [Fact]
    public void Bounds_MeshFailuresRankByCountThenByTheirWordsInOrdinalOrder()
    {
        var failures = Statistics(
        [
            new(Category(MeshReadError.UnexpectedException), 2),
            new(new MeshFailureCategory(MeshReadError.Malformed, "EndOfStreamException"), 2),
            new(Category(MeshReadError.NotFound), 2),
            new(Category(MeshReadError.NoRootNode), 3),
        ]);

        Assert.Equal(
            ["Mesh failures: 3 no root node, 2 EndOfStreamException, 2 not found, 2 unexpected error."],
            LogSections.BuildBoundsSection(failures, TriangleFailureCounts.None, Normal).Lines.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bounds_DetailedLogMatchesTheExpectedOutput(bool withFailures)
    {
        var stats = Statistics(withFailures ? [new(Category(MeshReadError.NotFound), 2)] : []);

        AssertExpectedOutput(LogSections.BuildBoundsSection(stats, TriangleFailureCounts.None, Detailed).Lines.ToList(), "Bounds_DetailedLogMatchesTheExpectedOutput", $"{withFailures}");
    }

    [Fact]
    public void Summaries_AlwaysShowAndMatchTheExpectedOutput()
    {
        var (world, decisions) = RunRounds();
        var removals = RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestLeftBehind.PerformedWithoutFindings);
        var kept = RemovalList.Kept(decisions);
        var context = Detailed with { Shapes = TestVisibility.OfTaggedBasesOnly() };
        var targets = Enumerable.Range(0, TargetCount)
            .Select(index => world.Targets[index] with
            {
                Base = TestVisibility.TaggedBase(index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible),
            })
            .ToList();

        AssertExpectedOutput(LogSections.BuildRemovalSummarySection(world, removals, decisions, TestTargets.TargetName, StepOutcome.SwitchedOff).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "removal-summary");
        AssertExpectedOutput(LogSections.BuildKeptSummarySection(kept).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "kept-summary");
        AssertExpectedOutput(LogSections.BuildMarkersByTypeSection(targets, removals, context).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "markers");
        Assert.Equal(["Removed markers by type: 1 Invisible marker doors."], LogSections.BuildMarkersByTypeSection(targets, removals, context).Lines.ToList());
    }

    /// <summary>Each kind's log name is the name its tick box has under "Never remove these as left behind".</summary>
    [Fact]
    public void TheLogNamesEachInvisibleObjectKindAsTheSettingsScreenDoes()
    {
        foreach (var kind in Enum.GetValues<InvisibleObjectKind>())
        {
            var property = typeof(KeptInvisibleObjectSettings).GetProperty(kind.ToString())!;

            Assert.Equal(property.GetCustomAttribute<SynthesisSettingName>()!.Name, ObjectVisibilityText.KindName(kind));
        }
    }

    [Fact]
    public void Hints_MatchTheExpectedOutput()
    {
        var (world, _) = RunRounds();
        ImmutableArray<ManualPatchHint> hints =
        [
            new(ManualPatchHintType.RemovedMarker, 0, "DoorMarkers"),
            new(ManualPatchHintType.KeptForNonPlacedReference, Protected, TestKeepReasons.QuestDetail),
        ];

        AssertExpectedOutput(LogSections.BuildHintsSection(world, hints).Lines.ToList(), "Hints_MatchTheExpectedOutput");
    }

    [Fact]
    public void Hints_HaveNoLinesWithoutRows()
    {
        var (world, _) = RunRounds();

        Assert.True(LogSections.BuildHintsSection(world, []).Lines.IsEmpty);
    }

    [Fact]
    public void Written_MergesTheTouchTablesAndOrdersTheLines()
    {
        var tables = ImmutableArray.Create(
            Table(ReportFileNames.EdgesFileName, 7, TimeSpan.FromSeconds(1)),
            Table(ReportFileNames.ComponentsFileName, 2, TimeSpan.FromSeconds(0.5)),
            Table(ReportFileNames.SupportFileName, 4),
            Table(ReportFileNames.MeshOriginsFileName, 2),
            Table(ReportFileNames.LeftBehindFileName, 6),
            Table(ReportFileNames.HintsFileName, 3),
            Table(ReportFileNames.RemovalsFileName, 9));

        var detailed = LogSections.BuildWrittenSection(Performed(tables), WithMeshOrigins(), Target, Detailed).Lines.ToList();
        Assert.Equal(
            [
                $"Touch diagnostics: wrote 7 edges, 2 components in 1.5s to {PathOf(ReportFileNames.EdgesFileName)} / {PathOf(ReportFileNames.ComponentsFileName)}.",
                $"Anchoring diagnostics: wrote 4 evaluations to {PathOf(ReportFileNames.SupportFileName)}.",
                "Mesh origins: 2 meshes of Target.esp objects, 1 origin near bottom, 0 origin near centre, 1 other.",
                $"  Wrote 2 mesh origins to {PathOf(ReportFileNames.MeshOriginsFileName)}.",
                $"Invisible objects left behind diagnostics: wrote 6 evaluations to {PathOf(ReportFileNames.LeftBehindFileName)}.",
                $"Removals: wrote 9 rows to {PathOf(ReportFileNames.RemovalsFileName)}.",
                $"Removed markers and kept objects: wrote 3 rows to {PathOf(ReportFileNames.HintsFileName)}.",
            ],
            detailed);
        Assert.Equal(
            $"Touch diagnostics: wrote 7 edges, 2 components to {PathOf(ReportFileNames.EdgesFileName)} / {PathOf(ReportFileNames.ComponentsFileName)}.",
            LogSections.BuildWrittenSection(Performed(tables), WithMeshOrigins(), Target, Normal).Lines.ToList()[0]);
    }

    [Fact]
    public void Written_ShowsTheMeshOriginSummaryEvenWhenItsFileWasNotWritten()
    {
        var lines = LogSections.BuildWrittenSection(Performed([]), WithMeshOrigins(), Target, Detailed).Lines.ToList();

        Assert.Equal(["Mesh origins: 2 meshes of Target.esp objects, 1 origin near bottom, 0 origin near centre, 1 other."], lines);
    }

    [Fact]
    public void Written_HidesTheMeshOriginSummaryInTheNormalLog()
    {
        Assert.Empty(LogSections.BuildWrittenSection(Performed([]), WithMeshOrigins(), Target, Normal).Lines.ToList());
    }

    [Fact]
    public void Written_IsEmptyWithoutReportFiles()
    {
        var switchedOff = new ReportFilesResult(StepOutcome.SwitchedOff, [], [], Failure: null);

        Assert.Empty(LogSections.BuildWrittenSection(switchedOff, SupportScene().AlsoRemove, Target, Detailed).Lines.ToList());
    }

    [Fact]
    public void Written_AStoppedWriterPrintsTheNotPerformedLine()
    {
        var lines = LogSections.BuildWrittenSection(
            new ReportFilesResult(StepOutcome.StoppedByUnexpectedError, [], [], Failure: null), SupportScene().AlsoRemove, Target, Normal).Lines.ToList();

        Assert.Equal(["Report files: not performed because of an unexpected error."], lines);
    }

    [Theory]
    [InlineData(StepOutcome.SwitchedOff, null)]
    [InlineData(StepOutcome.Performed, null)]
    [InlineData(StepOutcome.StoppedByUnexpectedError, "Report-file details: not performed because of an unexpected error.")]
    [InlineData(StepOutcome.StoppedByEarlierFailure, "Report-file details: not performed because Also remove was not performed.")]
    public void ExplanationProblems_HoldTheNotPerformedLineOnlyWhenTheDetailsWereStopped(StepOutcome detailsOutcome, string? notPerformedLine)
    {
        var lines = LogSections.BuildExplanationProblemsSection(detailsOutcome).Lines.ToList();

        string[] expected = notPerformedLine is null ? [] : [notPerformedLine];
        Assert.Equal(expected, lines);
    }

    private static ReportFilesResult Performed(ImmutableArray<WrittenTable> tables) => new(StepOutcome.Performed, tables, [], Failure: null);

    private static PartFailure Broke(PartKey part) => new(part, "it broke", Subject: null);

    [Fact]
    public void Done_ShowsTheTimeOnlyInTheDetailedLog()
    {
        Assert.Equal(["Done."], LogSections.BuildDoneSection(Elapsed, Normal).Lines.ToList());
        Assert.Equal(["Done in 1.5s."], LogSections.BuildDoneSection(Elapsed, Detailed).Lines.ToList());
    }

    private static WrittenTable Table(string fileName, int rows, TimeSpan? elapsed = null) =>
        new(fileName, rows, PathOf(fileName), elapsed ?? TimeSpan.Zero);

    private static string PathOf(string fileName) => $"reports/{fileName}";

    /// <summary>The support step with seeds whose report-file details were performed and hold two mesh origins.</summary>
    private static RestingObjectsResult WithMeshOrigins() =>
        SupportScene().AlsoRemove with
        {
            ReportFileDetails = new ReportFileDetails(
                StepOutcome.Performed,
                null,
                [MeshOriginOf(MeshOriginPlace.NearBottom), MeshOriginOf(MeshOriginPlace.Other)],
                Failure: null),
        };

    private static MeshOrigin MeshOriginOf(MeshOriginPlace origin) => new("m.nif", [], 1, default, Vector3.Zero, origin);

    private static BoundsStats Statistics(IReadOnlyList<KeyValuePair<MeshFailureCategory, int>> failures) =>
        new(10, 1, 2, 3, 1, 8, failures.Sum(failure => failure.Value), 1, 5, 3, 1, 2, failures);

    private static MeshFailureCategory Category(MeshReadError kind) => new(kind, ExceptionType: null);

    private static TouchChainSet TouchChains() => new(TouchStatistics, [], [], [], []);

    private static (CollectedObjects World, RestingObjectsResult AlsoRemove, RemovalDecisions Decisions) TouchScene()
    {
        var (world, decisions) = RunRounds(alsoRemoveReason: index => new RemovalReason.Touching(new TargetId(0)));
        return (world, AlsoRemoveOf(FollowUpRemovalMode.EverythingTouchingIt, decisions), decisions);
    }

    private static (CollectedObjects World, RestingObjectsResult AlsoRemove, RemovalDecisions Decisions) SupportScene()
    {
        var (world, decisions) = RunRounds(alsoRemoveReason: index => new RemovalReason.LostSupport(0.6f, new TargetId(0)));
        return (world, AlsoRemoveOf(FollowUpRemovalMode.OnlyWhatLosesItsSupport, decisions), decisions);
    }

    private static RestingObjectsResult AlsoRemoveOf(FollowUpRemovalMode mode, RemovalDecisions decisions) =>
        new(
            StepOutcome.Performed,
            mode,
            HadSeeds: true,
            [decisions.Rounds[0]],
            [decisions.Rounds[^1]],
            [],
            [],
            new RestingObjectsWork(1, 3, 4, 1, Pairs),
            Elapsed,
            TouchChains(),
            Failure: null,
            TouchChainsFailure: null,
            ReportFileDetails.SwitchedOff);

    private static (CollectedObjects World, LeftBehindResult LeftBehind) LeftBehindScene()
    {
        var (world, _) = RunRounds();
        LeftBehindCheck[] evaluations =
        [
            new(Protected, InvisibleObjectKind.MapMarkers, 500f, null, Surroundings(), LeftBehindOutcome.KeptProtectedType, null),
            new(ProtectedAlsoRemove, InvisibleObjectKind.XMarkers, 300f, world.OtherModObjects[0], Surroundings(), LeftBehindOutcome.RemovedInsideOtherObject, null),
        ];
        return (world, new LeftBehindResult(StepOutcome.Performed, evaluations, [], Failure: null));
    }

    private static SectorAreas Surroundings()
    {
        var tally = new SectorAreaTally(50);
        tally.Add(DirectionSector.East, 400f, removed: true);
        tally.Add(DirectionSector.North, 100f, removed: false);
        return tally.Build();
    }

    private static (CollectedObjects World, MarkerMoves MarkerMoves) MarkerMovesScene()
    {
        var (world, leftBehind) = LeftBehindScene();
        var inside = leftBehind.Evaluations[1];
        var target = new TargetId(inside.TargetIndex);
        MarkerMove[] moves =
        [
            new(target, new Vector3(100, 100, 0), new Vector3(300, 100, 0)),
            new(target, new Vector3(100, 100, 0), new Vector3(5000, -100, 0)),
        ];
        MarkerMoveDetail[] details =
        [
            new(inside, RelocationSurface.Navmesh, LeftHomeCell: false),
            new(inside, RelocationSurface.Terrain, LeftHomeCell: true),
        ];
        return (world, new MarkerMoves(StepOutcome.Performed, [.. moves], details, [inside], MarkerMoveLimits.MaxDistance, Failure: null));
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 follows from 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (CollectedObjects World,RemovalDecisions Decisions) RunRounds(Func<int, RemovalReason>? alsoRemoveReason = null)
    {
        var reasonOf = alsoRemoveReason ?? (_ => new RemovalReason.Touching(new TargetId(0)));
        var targets = TestTargets.CreateMany(TargetCount);
        var protection = ObjectsToKeep.Build(
            [.. targets],
            [TestTargets.Link(1, LinkedPartner)],
            TestTargets.References(
                TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedAlsoRemove] = QuestReason }),
            TestBaseFacts.Unresolved);
        var world = new CollectedObjects(
            [.. targets],
            [CreateOtherModObject()],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
        var decisions = RemovalDecisions.Start(protection, TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize())), Propose(Protected, new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, reasonOf(1)), Propose(ProtectedAlsoRemove, reasonOf(ProtectedAlsoRemove))]);
        return (world, decisions);
    }

    private static ProposedRemoval Propose(int index, RemovalReason reason) => new(new TargetId(index), reason);

    private static OtherObject CreateOtherModObject() =>
        new(
            new OtherId(0),
            new FormKey(OtherMod, 0x10).ToRecordKey(),
            TestTargets.Space,
            OtherMod.ToPluginName(),
            EditorId: null,
            Base: null,
            Vector3.Zero,
            default(Vector3),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false,
            Change: new ObjectChange(OtherMod.ToPluginName(), AcceptedFrom: null));

    private static (CollectedObjects World, RestingObjectsResult AlsoRemove, IRemovalDecisions Decisions) RunFixture(Settings settings)
    {
        var outcome = FixtureRun.Execute(settings, workers: 1);
        return (outcome.Collected, outcome.RestingObjects, outcome.Final);
    }

    private static void AssertExpectedOutput(IReadOnlyList<string> actual, string name, string part = "") =>
        ExpectedOutputFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
