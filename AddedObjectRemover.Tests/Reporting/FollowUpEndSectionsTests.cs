using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using P3Float = Noggog.P3Float;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The follow-up, leftover, relocation and closing log sections: what each log mode shows, where the
/// problems go, and that the detailed log reads exactly like its golden text.
/// </summary>
public sealed class FollowUpEndSectionsTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedFollowUp = 4;
    private const int LinkedPartner = 2;

    private static readonly KeepReason QuestReason = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST");
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    private static readonly ArchiveProblem ArchiveWarning = new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "Warning: archive a.bsa is unreadable.");
    private static readonly AssetProblem MeshWarning = new("m.nif", AssetProblemKind.NotFound, "[mesh] m.nif not found.");
    private static readonly PhaseProblems Problems = new([ArchiveWarning], [MeshWarning]);
    private static readonly TriangleStoreStats Meshes = new(Built: 3, Rebuilt: 1, TooLarge: 2, Evicted: 1, Triangles: 900, PeakResidentMeshes: 2, PeakResidentBytes: 5_000_000);
    private static readonly PairTestStats Pairs = new(PairsTested: 10, TouchingPairs: 4, PairsWithoutGeometry: 1, TrianglePairsTested: 77);
    private static readonly TouchStats TouchStatistics = new(Components: 1, ComponentsWithRemovals: 1, LargestComponent: 3, Levels: 1, MaxDepth: 1, Pairs);
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    [Theory]
    [InlineData(SettingsVariants.TouchWithLeftoversAndRelocation, 4, 1)]
    [InlineData(SettingsVariants.Support, 1, 0)]
    public void FollowUpCounts_MatchTheGoldenLogs(string variant, int removed, int held)
    {
        var (world, followUp) = RunFixture(SettingsVariants.Of(variant));
        var execution = new Execution(1);
        var lines = variant == SettingsVariants.Support
            ? LogSections.Anchoring(world, followUp, NoProblems, PhaseTimes.None, default, Detailed).Lines.ToList()
            : LogSections.Touch(world, followUp, FollowUp.Components(followUp, world.Targets.Length, execution), NoProblems, PhaseTimes.None, default, Detailed).Lines.ToList();

        var summary = lines.Single(line => line.StartsWith("Touching objects: ", StringComparison.Ordinal) || line.StartsWith("Anchoring: ", StringComparison.Ordinal));
        Assert.Contains($": {removed} removed ", summary);
        Assert.EndsWith($" {held} kept as referenced.", summary);
    }

    [Fact]
    public void Touch_CountsOnlyTouchingRemovalsAndHeldObjects()
    {
        var (world, followUp) = TouchScene();

        var summary = LogSections.Touch(world, followUp, TouchComponents(), NoProblems, PhaseTimes.None, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Touching objects: 1 removed in 1 components", summary);
        Assert.EndsWith("; 1 kept as referenced.", summary);
    }

    [Fact]
    public void Touch_NormalLogShowsKeptAndArchiveProblemsOnly()
    {
        var (world, followUp) = TouchScene();

        var lines = LogSections.Touch(world, followUp, components: null, Problems, PhaseTimes.None, Meshes, Normal).Lines.ToList();

        Assert.Equal(
            [
                $"  Kept {RecordNames.Describe(world.Targets[ProtectedFollowUp])} in Tamriel: linked from QUST (touches removed {RecordNames.Describe(world.Targets[0])}).",
                ArchiveWarning.Message,
            ],
            lines);
    }

    [Fact]
    public void Touch_DetailedLogPutsProblemsBetweenKeptAndStatisticsAndDropsTheEdgesTiming()
    {
        var (world, followUp) = TouchScene();
        var clock = new PhaseClock();
        clock.Time(TimedPhase.TouchSetup, () => { });

        var lines = LogSections.Touch(world, followUp, TouchComponents(), Problems, clock.Times(), Meshes, Detailed).Lines.ToList();

        Assert.StartsWith("  Kept ", lines[0]);
        Assert.Equal([ArchiveWarning.Message, MeshWarning.Message], lines.Skip(1).Take(2));
        Assert.StartsWith("Touching objects: ", lines[3]);
        Assert.Equal("  Timing: setup 0.0s, broad phase 0.0s, narrow phase 0.0s.", lines[^1]);
    }

    [Fact]
    public void Touch_DetailedLogMatchesTheGolden()
    {
        var (world, followUp) = TouchScene();
        var components = TouchComponents();

        AssertGolden(LogSections.Touch(world, followUp, components, Problems, PhaseTimes.None, Meshes, Detailed).Lines.ToList(), "Touch_DetailedLogMatchesTheGolden");
    }

    [Fact]
    public void Anchoring_CountsOnlyLostSupportRemovalsAndHeldObjects()
    {
        var (world, followUp) = AnchoringScene();

        var summary = LogSections.Anchoring(world, followUp, NoProblems, PhaseTimes.None, Meshes, Detailed).Lines.ToList()[1];

        Assert.StartsWith("Anchoring: 1 removed over 1 iterations; 3 touching objects evaluated (4 evaluations), 1 kept without any contact points, 1 kept as referenced.", summary);
    }

    [Fact]
    public void Anchoring_NormalLogShowsKeptAndArchiveProblemsOnly()
    {
        var (world, followUp) = AnchoringScene();

        var lines = LogSections.Anchoring(world, followUp, Problems, PhaseTimes.None, Meshes, Normal).Lines.ToList();

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("  Kept ", lines[0]);
        Assert.Equal(ArchiveWarning.Message, lines[1]);
    }

    [Fact]
    public void Anchoring_DetailedLogMatchesTheGolden()
    {
        var (world, followUp) = AnchoringScene();


        AssertGolden(LogSections.Anchoring(world, followUp, Problems, PhaseTimes.None, Meshes, Detailed).Lines.ToList(), "Anchoring_DetailedLogMatchesTheGolden");
    }

    [Fact]
    public void Leftovers_NormalLogShowsKeptProblemsAndTheSummaryWithoutTiming()
    {
        var (world, leftovers) = LeftoverScene();

        var lines = LogSections.Leftovers(world, leftovers, [new KeptTarget(Protected, QuestReason, null)], Problems, Elapsed, Normal).Lines.ToList();

        Assert.Equal(3, lines.Count);
        Assert.StartsWith("  Kept ", lines[0]);
        Assert.Equal(ArchiveWarning.Message, lines[1]);
        Assert.StartsWith("Leftover invisible objects: 2 evaluated; removed 1 (", lines[2]);
        Assert.DoesNotContain(" in 1.5s", lines[2]);
    }

    [Fact]
    public void Leftovers_DetailedLogMatchesTheGolden()
    {
        var (world, leftovers) = LeftoverScene();
        KeptTarget[] kept = [new(Protected, QuestReason, null)];


        AssertGolden(LogSections.Leftovers(world, leftovers, kept, Problems, Elapsed, Detailed).Lines.ToList(), "Leftovers_DetailedLogMatchesTheGolden");
    }

    [Fact]
    public void Relocations_NormalLogKeepsWarningsAndLeftInPlaceLinesAndSummaryOnly()
    {
        var (world, relocations) = RelocationScene();

        var lines = LogSections.Relocations(world, relocations, new RelocationOptions(), Problems, Normal).Lines.ToList();

        Assert.Equal(4, lines.Count);
        Assert.Equal(ArchiveWarning.Message, lines[0]);
        Assert.StartsWith("  Warning: ", lines[1]);
        Assert.StartsWith("  Left kept ", lines[2]);
        Assert.Equal("Moved 2 kept markers out of other mods' objects; 1 left in place.", lines[3]);
    }

    [Fact]
    public void Relocations_DetailedLogMatchesTheGolden()
    {
        var (world, relocations) = RelocationScene();


        AssertGolden(LogSections.Relocations(world, relocations, new RelocationOptions(), Problems, Detailed).Lines.ToList(), "Relocations_DetailedLogMatchesTheGolden");
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
        AssertGolden(LogSections.Write(written, Elapsed, Detailed).Lines.ToList(), "Write_ShowsTheTimeOnlyInTheDetailedLogAndTheEnableParentLineAlways");
    }

    [Fact]
    public void Write_OmitsTheEnableParentLineWhenNoneWasReplaced()
    {
        Assert.Single(LogSections.Write(new WriteSummary(1, 0, 0), Elapsed, Normal).Lines.ToList());
    }

    [Fact]
    public void BoundsIndexTimes_AreDetailedOnlyAndMatchTheGolden()
    {
        var clock = new PhaseClock();
        clock.Time(TimedPhase.RivalBoundsBuild, () => { });
        clock.Time(TimedPhase.SolidBoundsBuild, () => { });

        Assert.Empty(LogSections.BoundsIndexTimes(clock.Times(), Normal).Lines.ToList());
        AssertGolden(LogSections.BoundsIndexTimes(clock.Times(), Detailed).Lines.ToList(), "BoundsIndexTimes_AreDetailedOnlyAndMatchTheGolden");
    }

    [Fact]
    public void Removals_AndSpaces_AreDetailedOnlyAndMatchTheGolden()
    {
        var (world, ledger) = RunRounds();
        var removals = Decisions.Removals(ledger, world, LeftoverResult.None);
        var detailed = Detailed with { Bases = null! };

        Assert.Empty(LogSections.Removals(world, removals, Normal).Lines.ToList());
        Assert.Empty(LogSections.Spaces(world, removals, Normal).Lines.ToList());
        AssertGolden(LogSections.Removals(world, removals, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheGolden", "removals");
        AssertGolden(LogSections.Spaces(world, removals, detailed).Lines.ToList(), "Removals_AndSpaces_AreDetailedOnlyAndMatchTheGolden", "spaces");
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
    public void Bounds_DetailedLogMatchesTheGolden(bool withFailures)
    {
        var stats = Statistics(withFailures ? [new("not found", 2)] : []);

        AssertGolden(LogSections.Bounds(stats, Detailed).Lines.ToList(), "Bounds_DetailedLogMatchesTheGolden", $"{withFailures}");
    }

    [Fact]
    public void Summaries_AlwaysShowAndMatchTheGolden()
    {
        var (world, ledger) = RunRounds();
        var removals = Decisions.Removals(ledger, world, LeftoverResult.None);
        var kept = Decisions.Kept(ledger);
        var looks = new TargetLooks([.. Enumerable.Range(0, TargetCount).Select(index =>
            index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible)]);

        AssertGolden(LogSections.RemovalSummary(removals).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheGolden", "removal-summary");
        AssertGolden(LogSections.KeptSummary(kept).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheGolden", "kept-summary");
        AssertGolden(LogSections.MarkersByType(removals, looks).Lines.ToList(), "Summaries_AlwaysShowAndMatchTheGolden", "markers");
        Assert.Equal(["Removed markers by type: 1 DoorMarkers."], LogSections.MarkersByType(removals, looks).Lines.ToList());
    }

    [Fact]
    public void Hints_AlwaysShowAndMatchTheGolden()
    {
        var (world, _) = RunRounds();
        ImmutableArray<ManualPatchHint> hints =
        [
            new(ManualPatchHintType.RemovedMarker, 0, "DoorMarkers"),
            new(ManualPatchHintType.KeptForNonPlacedReference, Protected, "linked from QUST"),
        ];

        AssertGolden(LogSections.Hints(world, hints).Lines.ToList(), "Hints_AlwaysShowAndMatchTheGolden");
    }

    [Fact]
    public void Written_MergesTheTouchTablesAndOrdersTheLines()
    {
        var tables = ImmutableArray.Create(
            Table(Tables.EdgesFileName, 7, TimeSpan.FromSeconds(1)),
            Table(Tables.ComponentsFileName, 2, TimeSpan.FromSeconds(0.5)),
            Table(Tables.AnchoringFileName, 4),
            Table(Tables.MeshOriginsFileName, 2),
            Table(Tables.LeftoversFileName, 6),
            Table(Tables.HintsFileName, 3));

        var detailed = LogSections.Written(tables, WithMeshOrigins(), Detailed).Lines.ToList();

        Assert.Equal(
            [
                $"Touch diagnostics: wrote 7 edges, 2 components in 1.5s to {PathOf(Tables.EdgesFileName)} / {PathOf(Tables.ComponentsFileName)}.",
                $"Anchoring diagnostics: wrote 4 evaluations to {PathOf(Tables.AnchoringFileName)}.",
                "Mesh origins: 2 target meshes, 1 origin near bottom, 0 origin near centre, 1 other.",
                $"  Wrote 2 mesh origins to {PathOf(Tables.MeshOriginsFileName)}.",
                $"Leftover invisible objects diagnostics: wrote 6 evaluations to {PathOf(Tables.LeftoversFileName)}.",
                $"Manual patch hints: wrote 3 rows to {PathOf(Tables.HintsFileName)}.",
            ],
            detailed);
        Assert.Equal(
            $"Touch diagnostics: wrote 7 edges, 2 components to {PathOf(Tables.EdgesFileName)} / {PathOf(Tables.ComponentsFileName)}.",
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
        Assert.Empty(LogSections.Written([], Explanations.None, Detailed).Lines.ToList());
    }

    [Fact]
    public void Done_ShowsTheTimeOnlyInTheDetailedLog()
    {
        Assert.Equal(["Done."], LogSections.Done(Elapsed, Normal).Lines.ToList());
        Assert.Equal(["Done in 1.5s."], LogSections.Done(Elapsed, Detailed).Lines.ToList());
    }

    private static PhaseProblems NoProblems => new([], []);

    private static WrittenTable Table(string fileName, int rows, TimeSpan? elapsed = null) =>
        new(fileName, rows, PathOf(fileName), elapsed ?? TimeSpan.Zero);

    private static string PathOf(string fileName) => $"reports/{fileName}";

    private static Explanations WithMeshOrigins() =>
        new(null, [MeshOriginOf(MeshOriginSurvey.NearBottom), MeshOriginOf(MeshOriginSurvey.Other)]);

    private static MeshOrigin MeshOriginOf(string origin) => new("m.nif", [], 1, default, Vector3.Zero, origin);

    private static BoundsStats Statistics(IReadOnlyList<KeyValuePair<string, int>> failures) =>
        new(10, 1, 2, 3, 1, 8, failures.Sum(failure => failure.Value), 1, 5, 3, 1, 2, failures);

    private static TouchComponentSet TouchComponents() => new(TouchStatistics, [], [], [], []);

    private static (World World, FollowUpResult FollowUp) TouchScene()
    {
        var (world, ledger) = RunRounds(followUpCause: index => new Cause.Touching(new TargetId(0)));
        return (world, FollowUpOf(FollowUpRemovalMode.EverythingTouching, ledger));
    }

    private static (World World, FollowUpResult FollowUp) AnchoringScene()
    {
        var (world, ledger) = RunRounds(followUpCause: index => new Cause.LostSupport(0.6f, new TargetId(0)));
        return (world, FollowUpOf(FollowUpRemovalMode.ObjectsSupportedByIt, ledger));
    }

    private static FollowUpResult FollowUpOf(FollowUpRemovalMode mode, Ledger ledger) =>
        new(mode, HadSeeds: true, ledger, [ledger.Rounds[^1]], [], new FollowUpWork(1, 3, 4, 1, Pairs), Context: null);

    private static (World World, LeftoverResult Leftovers) LeftoverScene()
    {
        var (world, _) = RunRounds();
        LeftoverEvaluation[] evaluations =
        [
            new(Protected, InvisibleObjectKind.MapMarkers, 500f, null, Surroundings(), LeftoverDecision.KeptProtectedType, null),
            new(ProtectedFollowUp, InvisibleObjectKind.XMarkers, 300f, world.Rivals[0], Surroundings(), LeftoverDecision.RemovedInsideOtherObject, null),
        ];
        return (world, new LeftoverResult(evaluations));
    }

    private static SectorAreas Surroundings()
    {
        var areas = new SectorAreas(50);
        areas.Add(DirectionSector.East, 400f, removed: true);
        areas.Add(DirectionSector.North, 100f, removed: false);
        return areas;
    }

    private static (World World, RelocationResult Relocations) RelocationScene()
    {
        var (world, leftovers) = LeftoverScene();
        var inside = leftovers.Evaluations[1];
        var moves = new Relocation[]
        {
            new(inside, new Vector3(100, 100, 0), new Vector3(300, 100, 0), RelocationSurface.Navmesh, LeftHomeCell: false),
            new(inside, new Vector3(100, 100, 0), new Vector3(5000, -100, 0), RelocationSurface.Terrain, LeftHomeCell: true),
        };
        return (world, new RelocationResult(moves, [inside]));
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 follows from 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (World World, Ledger Ledger) RunRounds(Func<int, Cause>? followUpCause = null)
    {
        var causeOf = followUpCause ?? (_ => new Cause.Touching(new TargetId(0)));
        var targets = TestTargets.CreateMany(TargetCount);
        var references = TestTargets.References(
            TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedFollowUp] = QuestReason });
        var world = new World(
            [.. targets],
            [CreateRival()],
            Collected<ImmutableArray<OtherObject>>.NotCollected,
            [TestTargets.Link(1, LinkedPartner)],
            [.. references],
            new Dictionary<FormKey, string> { [TestTargets.Space] = "Tamriel" },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0),
            []);
        var ledger = Ledger.Start(Protection.Build(world.Targets, world.Links, world.References), TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new Cause.TooClose(new OtherId(0))), Propose(Protected, new Cause.TooClose(new OtherId(0)))])
            .Apply(RoundKind.FollowUp, [Propose(1, causeOf(1)), Propose(ProtectedFollowUp, causeOf(ProtectedFollowUp))]);
        return (world, ledger);
    }

    private static Proposal Propose(int index, Cause cause) => new(new TargetId(index), cause);

    private static OtherObject CreateRival() =>
        new(
            new OtherId(0),
            new FormKey(OtherMod, 0x10),
            TestTargets.Space,
            OtherMod,
            EditorId: null,
            Base: null,
            Vector3.Zero,
            default(P3Float),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);

    private static (World World, FollowUpResult FollowUp) RunFixture(Settings settings)
    {
        var outcome = FixtureRun.Execute(settings, workers: 1);
        return (outcome.World, outcome.FollowUp);
    }

    private static void AssertGolden(IReadOnlyList<string> actual, string name, string part = "") =>
        GoldenFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
