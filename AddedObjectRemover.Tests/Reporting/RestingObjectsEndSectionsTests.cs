using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The also-remove, left-behind, marker-move and closing log sections: what each log mode shows, where the
/// problems go, and that the detailed log reads exactly like its expected output.
/// </summary>
public sealed class RestingObjectsEndSectionsTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedAlsoRemove = 4;
    private const int LinkedPartner = 2;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    private static readonly AssetProblem MeshWarning = new("m.nif", AssetProblemKind.NotFound, "[mesh] m.nif not found.");
    private static readonly PhaseProblems Problems = new([MeshWarning]);
    private static readonly TriangleStoreStats Meshes = new(Built: 3, TooLarge: 2, Triangles: 900);
    private static readonly PairTestStats Pairs = new(PairsTested: 10, TouchingPairs: 4, PairsWithoutGeometry: 1, TrianglePairsTested: 77);
    private static readonly TouchChainStatistics TouchStatistics = new(Components: 1, ComponentsWithRemovals: 1, LargestComponent: 3, Levels: 1, MaxDepth: 1, Pairs);
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    [Theory]
    [InlineData(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, 4, 1)]
    [InlineData(SettingsVariants.Support, 1, 0)]
    public void AlsoRemoveCounts_MatchTheExpectedOutputLogs(string variant, int removed, int held)
    {
        var (world, alsoRemove) = RunFixture(SettingsVariants.Of(variant));
        var lines = variant == SettingsVariants.Support
            ? LogSections.Anchoring(world, alsoRemove, NoProblems, Elapsed, default, Detailed).Lines.ToList()
            : LogSections.Touch(world, alsoRemove, TouchChains(), NoProblems, Elapsed, default, Detailed).Lines.ToList();

        var summary = lines.Single(line => line.StartsWith("Touching objects: ", StringComparison.Ordinal) || line.StartsWith("Anchoring: ", StringComparison.Ordinal));
        Assert.Contains($": {removed} removed ", summary);
        Assert.EndsWith($" {held} kept as referenced.", summary);
    }

    [Fact]
    public void Touch_CountsOnlyTouchingRemovalsAndHeldObjects()
    {
        var (world, alsoRemove) = TouchScene();

        var summary = LogSections.Touch(world, alsoRemove, TouchChains(), NoProblems, Elapsed, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Touching objects: 1 removed in 1 components", summary);
        Assert.EndsWith("; 1 kept as referenced.", summary);
    }

    [Fact]
    public void Touch_NormalLogShowsKeptOnly()
    {
        var (world, alsoRemove) = TouchScene();

        var lines = LogSections.Touch(world, alsoRemove, touchChains: null, Problems, Elapsed, Meshes, Normal).Lines.ToList();

        Assert.Equal(
            [
                $"  Kept {RecordNames.Describe(world.Targets[ProtectedAlsoRemove])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail} (touches removed {RecordNames.Describe(world.Targets[0])}).",
            ],
            lines);
    }

    [Fact]
    public void Touch_DetailedLogLeavesOutTheChainStatisticsWhenTheChainsCouldNotBeFound()
    {
        var (world, alsoRemove) = TouchScene();

        var lines = LogSections.Touch(world, alsoRemove, touchChains: null, Problems, Elapsed, Meshes, Detailed).Lines.ToList();

        Assert.DoesNotContain(lines, line => line.StartsWith("Touching objects: ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("  Pairs: ", StringComparison.Ordinal));
        Assert.Equal("Also remove: 1 rounds in 1.5s.", lines[^1]);
    }

    [Fact]
    public void Touch_DetailedLogPutsProblemsBetweenKeptAndStatisticsAndEndsWithTheAlsoRemoveTime()
    {
        var (world, alsoRemove) = TouchScene();

        var lines = LogSections.Touch(world, alsoRemove, TouchChains(), Problems, Elapsed, Meshes, Detailed).Lines.ToList();

        Assert.StartsWith("  Kept ", lines[0]);
        Assert.Equal(MeshWarning.Message, lines[1]);
        Assert.StartsWith("Touching objects: ", lines[2]);
        Assert.Equal("Also remove: 1 rounds in 1.5s.", lines[^1]);
    }

    [Fact]
    public void Touch_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, alsoRemove) = TouchScene();
        var touchChains = TouchChains();

        AssertExpectedOutput(LogSections.Touch(world, alsoRemove, touchChains, Problems, Elapsed, Meshes, Detailed).Lines.ToList(), "Touch_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Anchoring_CountsOnlyLostSupportRemovalsAndHeldObjects()
    {
        var (world, alsoRemove) = AnchoringScene();

        var summary = LogSections.Anchoring(world, alsoRemove, NoProblems, Elapsed, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Anchoring: 1 removed over 1 iterations; 3 touching objects evaluated (4 evaluations), 1 kept without any contact points, 1 kept as referenced.", summary);
    }

    [Fact]
    public void Anchoring_NormalLogShowsKeptOnly()
    {
        var (world, alsoRemove) = AnchoringScene();

        var lines = LogSections.Anchoring(world, alsoRemove, Problems, Elapsed, Meshes, Normal).Lines.ToList();

        Assert.StartsWith("  Kept ", Assert.Single(lines));
    }

    [Fact]
    public void Anchoring_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, alsoRemove) = AnchoringScene();


        AssertExpectedOutput(LogSections.Anchoring(world, alsoRemove, Problems, Elapsed, Meshes, Detailed).Lines.ToList(), "Anchoring_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Leftovers_NormalLogShowsKeptProblemsAndTheSummaryWithoutTiming()
    {
        var (world, leftBehind) = LeftBehindScene();

        var lines = LogSections.LeftBehind(world, leftBehind, [new KeptObject(Protected, QuestReason, null)], Problems, Elapsed, Normal).Lines.ToList();

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("  Kept ", lines[0]);
        Assert.StartsWith("Leftover invisible objects: 2 evaluated; removed 1 (", lines[1]);
        Assert.DoesNotContain(" in 1.5s", lines[1]);
    }

    [Fact]
    public void Leftovers_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, leftBehind) = LeftBehindScene();
        KeptObject[] kept = [new(Protected, QuestReason, null)];


        AssertExpectedOutput(LogSections.LeftBehind(world, leftBehind, kept, Problems, Elapsed, Detailed).Lines.ToList(), "Leftovers_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Relocations_NormalLogKeepsWarningsAndLeftInPlaceLinesAndSummaryOnly()
    {
        var (world, markerMoves) = MarkerMovesScene();

        var lines = LogSections.KeptMarkerMoves(world, markerMoves, new MarkerMoveSettings(MarkerMoveLimits.MaxDistance), Problems, Normal).Lines.ToList();

        Assert.Equal(3, lines.Count);
        Assert.StartsWith("  Warning: ", lines[0]);
        Assert.StartsWith("  Left kept ", lines[1]);
        Assert.Equal("Moved 2 kept markers out of other mods' objects; 1 left in place.", lines[2]);
    }

    [Fact]
    public void Relocations_DetailedLogMatchesTheExpectedOutput()
    {
        var (world, markerMoves) = MarkerMovesScene();


        AssertExpectedOutput(LogSections.KeptMarkerMoves(world, markerMoves, new MarkerMoveSettings(MarkerMoveLimits.MaxDistance), Problems, Detailed).Lines.ToList(), "Relocations_DetailedLogMatchesTheExpectedOutput");
    }

    [Fact]
    public void Write_ShowsTheTimeOnlyInTheDetailedLogAndTheEnableParentLineAlways()
    {
        var written = new WriteSummary(Removed: 5, Moved: 2, EnableParentsReplaced: 3);

        Assert.Equal(
            [
                "Wrote 7 overrides (5 removed, 2 moved).",
                "  3 removed objects had an Enable Parent; it was replaced by the player with \"opposite of parent\" so they stay disabled.",
            ],
            LogSections.Write(written, Elapsed, Normal).Lines.ToList());
        AssertExpectedOutput(LogSections.Write(written, Elapsed, Detailed).Lines.ToList(), "Write_ShowsTheTimeOnlyInTheDetailedLogAndTheEnableParentLineAlways");
    }

    [Fact]
    public void Write_OmitsTheEnableParentLineWhenNoneWasReplaced()
    {
        Assert.Single(LogSections.Write(new WriteSummary(1, 0, 0), Elapsed, Normal).Lines.ToList());
    }

    [Fact]
    public void Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput()
    {
        var (world, decisions) = RunRounds();
        var removals = RemovalList.Removals(decisions, world, LeftBehindResult.None);
        var detailed = Detailed with { Bases = null! };

        Assert.Empty(LogSections.Removals(world, removals, Normal).Lines.ToList());
        Assert.Empty(LogSections.Spaces(world, removals, Normal).Lines.ToList());
        AssertExpectedOutput(LogSections.Removals(world, removals, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput", "removals");
        AssertExpectedOutput(LogSections.Spaces(world, removals, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheExpectedOutput", "spaces");
    }

    [Fact]
    public void Bounds_NormalLogShowsOnlyNonEmptyMeshFailuresWithoutIndent()
    {
        var failures = Statistics([new("not found", 2), new("unreadable", 1)]);

        Assert.Equal(["Mesh failures: 2 not found, 1 unreadable."], LogSections.Bounds(failures, Normal).Lines.ToList());
        Assert.Empty(LogSections.Bounds(Statistics([]), Normal).Lines.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bounds_DetailedLogMatchesTheExpectedOutput(bool withFailures)
    {
        var stats = Statistics(withFailures ? [new("not found", 2)] : []);

        AssertExpectedOutput(LogSections.Bounds(stats, Detailed).Lines.ToList(), "Bounds_DetailedLogMatchesTheExpectedOutput", $"{withFailures}");
    }

    [Fact]
    public void Summaries_AlwaysShowAndMatchTheExpectedOutput()
    {
        var (world, decisions) = RunRounds();
        var removals = RemovalList.Removals(decisions, world, LeftBehindResult.None);
        var kept = RemovalList.Kept(decisions);
        var context = Detailed with { Shapes = TestVisibility.OfTaggedBasesOnly() };
        var targets = Enumerable.Range(0, TargetCount)
            .Select(index => world.Targets[index] with
            {
                Base = TestVisibility.TaggedBase(index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible),
            })
            .ToList();

        AssertExpectedOutput(LogSections.RemovalSummary(removals).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "removal-summary");
        AssertExpectedOutput(LogSections.KeptSummary(kept).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "kept-summary");
        AssertExpectedOutput(LogSections.MarkersByType(targets, removals, context).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheExpectedOutput", "markers");
        Assert.Equal(["Removed markers by type: 1 DoorMarkers."], LogSections.MarkersByType(targets, removals, context).Lines.ToList());
    }

    [Fact]
    public void Hints_AlwaysShowAndMatchTheExpectedOutput()
    {
        var (world, _) = RunRounds();
        ImmutableArray<ManualPatchHint> hints =
        [
            new(ManualPatchHintType.RemovedMarker, 0, "DoorMarkers"),
            new(ManualPatchHintType.KeptForNonPlacedReference, Protected, TestKeepReasons.QuestDetail),
        ];

        AssertExpectedOutput(LogSections.Hints(world, hints).Lines.ToList(), "Hints_AlwaysShowAndMatchTheExpectedOutput");
    }

    [Fact]
    public void Written_MergesTheTouchTablesAndOrdersTheLines()
    {
        var tables = ImmutableArray.Create(
            Table(ReportFileNames.EdgesFileName, 7, TimeSpan.FromSeconds(1)),
            Table(ReportFileNames.ComponentsFileName, 2, TimeSpan.FromSeconds(0.5)),
            Table(ReportFileNames.AnchoringFileName, 4),
            Table(ReportFileNames.MeshOriginsFileName, 2),
            Table(ReportFileNames.LeftBehindFileName, 6),
            Table(ReportFileNames.HintsFileName, 3));

        var detailed = LogSections.Written(tables, WithMeshOrigins(), Detailed).Lines.ToList();

        Assert.Equal(
            [
                $"Touch diagnostics: wrote 7 edges, 2 components in 1.5s to {PathOf(ReportFileNames.EdgesFileName)} / {PathOf(ReportFileNames.ComponentsFileName)}.",
                $"Anchoring diagnostics: wrote 4 evaluations to {PathOf(ReportFileNames.AnchoringFileName)}.",
                "Mesh origins: 2 target meshes, 1 origin near bottom, 0 origin near centre, 1 other.",
                $"  Wrote 2 mesh origins to {PathOf(ReportFileNames.MeshOriginsFileName)}.",
                $"Leftover invisible objects diagnostics: wrote 6 evaluations to {PathOf(ReportFileNames.LeftBehindFileName)}.",
                $"Manual patch hints: wrote 3 rows to {PathOf(ReportFileNames.HintsFileName)}.",
            ],
            detailed);
        Assert.Equal(
            $"Touch diagnostics: wrote 7 edges, 2 components to {PathOf(ReportFileNames.EdgesFileName)} / {PathOf(ReportFileNames.ComponentsFileName)}.",
            LogSections.Written(tables, WithMeshOrigins(), Normal).Lines.ToList()[0]);
    }

    [Fact]
    public void Written_ShowsTheMeshOriginSummaryEvenWhenItsFileWasNotWritten()
    {
        var lines = LogSections.Written([], WithMeshOrigins(), Detailed).Lines.ToList();

        Assert.Equal(["Mesh origins: 2 target meshes, 1 origin near bottom, 0 origin near centre, 1 other."], lines);
    }

    [Fact]
    public void Written_HidesTheMeshOriginSummaryInTheNormalLog()
    {
        Assert.Empty(LogSections.Written([], WithMeshOrigins(), Normal).Lines.ToList());
    }

    [Fact]
    public void Written_IsEmptyWithoutReportFiles()
    {
        Assert.Empty(LogSections.Written([], ReportFileDetails.None, Detailed).Lines.ToList());
    }

    [Fact]
    public void Done_ShowsTheTimeOnlyInTheDetailedLog()
    {
        Assert.Equal(["Done."], LogSections.Done(Elapsed, Normal).Lines.ToList());
        Assert.Equal(["Done in 1.5s."], LogSections.Done(Elapsed, Detailed).Lines.ToList());
    }

    private static PhaseProblems NoProblems => new([]);

    private static WrittenTable Table(string fileName, int rows, TimeSpan? elapsed = null) =>
        new(fileName, rows, PathOf(fileName), elapsed ?? TimeSpan.Zero);

    private static string PathOf(string fileName) => $"reports/{fileName}";

    private static ReportFileDetails WithMeshOrigins() =>
        new(null, [MeshOriginOf(MeshOriginPlaces.NearBottom), MeshOriginOf(MeshOriginPlaces.Other)]);

    private static MeshOrigin MeshOriginOf(string origin) => new("m.nif", [], 1, default, Vector3.Zero, origin);

    private static BoundsStats Statistics(IReadOnlyList<KeyValuePair<string, int>> failures) =>
        new(10, 1, 2, 3, 1, 8, failures.Sum(failure => failure.Value), 1, 5, 3, 1, 2, failures);

    private static TouchChainSet TouchChains() => new(TouchStatistics, [], [], [], []);

    private static (CollectedObjects World,RestingObjectsResult AlsoRemove) TouchScene()
    {
        var (world, decisions) = RunRounds(alsoRemoveReason: index => new RemovalReason.Touching(new TargetId(0)));
        return (world, AlsoRemoveOf(FollowUpRemovalMode.EverythingTouching, decisions));
    }

    private static (CollectedObjects World,RestingObjectsResult AlsoRemove) AnchoringScene()
    {
        var (world, decisions) = RunRounds(alsoRemoveReason: index => new RemovalReason.LostSupport(0.6f, new TargetId(0)));
        return (world, AlsoRemoveOf(FollowUpRemovalMode.ObjectsSupportedByIt, decisions));
    }

    private static RestingObjectsResult AlsoRemoveOf(FollowUpRemovalMode mode, RemovalDecisions decisions) =>
        new(mode, HadSeeds: true, decisions, [decisions.Rounds[^1]], [], new RestingObjectsWork(1, 3, 4, 1, Pairs));

    private static (CollectedObjects World,LeftBehindResult LeftBehind) LeftBehindScene()
    {
        var (world, _) = RunRounds();
        LeftBehindCheck[] evaluations =
        [
            new(Protected, InvisibleObjectKind.MapMarkers, 500f, null, Surroundings(), LeftBehindOutcome.KeptProtectedType, null),
            new(ProtectedAlsoRemove, InvisibleObjectKind.XMarkers, 300f, world.OtherModObjects[0], Surroundings(), LeftBehindOutcome.RemovedInsideOtherObject, null),
        ];
        return (world, new LeftBehindResult(evaluations));
    }

    private static SectorAreas Surroundings()
    {
        var tally = new SectorAreaTally(50);
        tally.Add(DirectionSector.East, 400f, removed: true);
        tally.Add(DirectionSector.North, 100f, removed: false);
        return tally.Build();
    }

    private static (CollectedObjects World,MarkerMoves MarkerMoves) MarkerMovesScene()
    {
        var (world, leftBehind) = LeftBehindScene();
        var inside = leftBehind.Evaluations[1];
        var moves = new KeptMarkerMove[]
        {
            new(inside, new Vector3(100, 100, 0), new Vector3(300, 100, 0), RelocationSurface.Navmesh, LeftHomeCell: false),
            new(inside, new Vector3(100, 100, 0), new Vector3(5000, -100, 0), RelocationSurface.Terrain, LeftHomeCell: true),
        };
        return (world, new MarkerMoves(moves, [inside]));
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
                TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedAlsoRemove] = QuestReason }));
        var world = new CollectedObjects(
            [.. targets],
            [CreateOtherModObject()],
            null,
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace) },
            new ReadCounts(0, 0, 0, 0, 0, 0),
            []);
        var decisions = RemovalDecisions.Start(protection, TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0))), Propose(Protected, new RemovalReason.TooClose(new OtherId(0)))])
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
            HasMapMarker: false);

    private static (CollectedObjects World,RestingObjectsResult AlsoRemove) RunFixture(Settings settings)
    {
        var outcome = FixtureRun.Execute(settings, workers: 1);
        return (outcome.World, outcome.RestingObjects);
    }

    private static void AssertExpectedOutput(IReadOnlyList<string> actual, string name, string part = "") =>
        ExpectedOutputFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
