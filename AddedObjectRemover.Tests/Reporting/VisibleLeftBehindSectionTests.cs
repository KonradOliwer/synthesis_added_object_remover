using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The log of the visible left-behind step: its summary, the objects worth a detailed line, the kept and linked lines and the stop line.</summary>
public sealed class VisibleLeftBehindSectionTests
{
    private const int TargetCount = 7;
    private const int Removed = 0;
    private const int Referenced = 1;
    private const int ProtectedKind = 2;
    private const int NearMiss = 3;
    private const int NoClearedDirection = 4;
    private const int TooFewObjects = 5;
    private const int LinkedPartner = 6;

    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    [Fact]
    public void TheSummaryCountsEveryOutcomeAndTheNormalLogShowsOnlyItAndTheKeptLines()
    {
        var (world, result, decisions) = Scene();

        var lines = LogSections.BuildVisibleLeftBehindSection(world, result, RemovalList.KeptIn(decisions, decisions.Rounds[^1]), Elapsed, Normal).Lines.ToList();

        Assert.Equal(
            "Visible objects left behind: 6 evaluated; removed 1 (1 surroundings removed); "
            + "kept 5 (1 too few surrounding objects, 2 surroundings mostly kept, 1 protected type, 1 referenced).",
            lines[0]);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("  Kept ", lines[1]);
    }

    [Fact]
    public void TheDetailedLogAddsALineForEveryRemovedReferencedProtectedAndNearMissObjectWithItsAreas()
    {
        var (world, result, decisions) = Scene();

        var lines = LogSections.BuildVisibleLeftBehindSection(world, result, RemovalList.KeptIn(decisions, decisions.Rounds[^1]), Elapsed, Detailed).Lines.ToList();

        Assert.Equal(
            "Visible objects left behind: 6 evaluated in 1.5s; removed 1 (1 surroundings removed); "
            + "kept 5 (1 too few surrounding objects, 2 surroundings mostly kept, 1 protected type, 1 referenced).",
            lines[0]);
        var detail = lines.Where(line => line.StartsWith("  Visible left behind: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, detail.Count);
        AssertLine(detail[0], world, result.Evaluations[Removed], "NPCs and creatures", "removed, surroundings removed");
        AssertLine(detail[1], world, result.Evaluations[Referenced], "Containers", "kept, referenced: ");
        AssertLine(detail[2], world, result.Evaluations[ProtectedKind], "Containers", "kept, protected type");
        AssertLine(detail[3], world, result.Evaluations[NearMiss], null, "kept, surroundings mostly kept");
        foreach (var index in new[] { NoClearedDirection, TooFewObjects })
        {
            Assert.DoesNotContain(detail, line => line.Contains(RecordNames.Describe(world.Targets[index]), StringComparison.Ordinal));
        }

        Assert.StartsWith("  Kept ", lines[^1]);
    }

    [Fact]
    public void TheLinkedLineNamesTheVisibleLeftBehindRemovals()
    {
        var (world, result, decisions) = Scene();

        var sections = TestReport.WithoutProblems().VisibleLeftBehindSections(result, decisions, world);

        Assert.Equal(["visible-leftovers", "linked", "unexpectedErrors"], sections.Select(section => section.Id));
        Assert.Equal("Linked groups: 1 more objects removed with the visible left-behind removals they are linked to.", Assert.Single(sections[1].Lines));
        Assert.Empty(sections[2].Lines);
    }

    [Fact]
    public void AnObjectRemovedAsALinkedMemberIsCountedAndPrintedAsRemoved()
    {
        var (world, scene, decisions) = Scene();
        var linkedMember = new VisibleLeftBehindCheck(
            LinkedPartner, null, Areas(northCleared: false), VisibleLeftBehindOutcome.RemovedLinkedToRemoval, null);
        var result = scene with { Evaluations = [.. scene.Evaluations, linkedMember] };

        var lines = LogSections.BuildVisibleLeftBehindSection(world, result, RemovalList.KeptIn(decisions, decisions.Rounds[^1]), Elapsed, Detailed).Lines.ToList();

        Assert.Equal(
            "Visible objects left behind: 7 evaluated in 1.5s; removed 2 (1 surroundings removed, 1 linked to a removed object); "
            + "kept 5 (1 too few surrounding objects, 2 surroundings mostly kept, 1 protected type, 1 referenced).",
            lines[0]);
        AssertLine(lines.Single(line => line.Contains(RecordNames.Describe(world.Targets[LinkedPartner]), StringComparison.Ordinal) && line.StartsWith("  Visible left behind", StringComparison.Ordinal)),
            world, linkedMember, null, "removed, linked to a removed object");
    }

    [Fact]
    public void ASwitchedOffStepPrintsNothing()
    {
        var (world, _, decisions) = Scene();

        Assert.Empty(TestReport.WithoutProblems().VisibleLeftBehindSections(VisibleLeftBehindResult.SwitchedOff, decisions, world));
    }

    [Fact]
    public void AStoppedStepPrintsTheNotPerformedLineAndItsError()
    {
        var (world, _, decisions) = Scene();
        var stopped = new VisibleLeftBehindResult(
            StepOutcome.StoppedByUnexpectedError,
            [],
            [],
            new PartFailure(VisibleLeftBehindParts.VisibleLeftBehind, "InvalidOperationException: it broke", Subject: null));

        var sections = TestReport.WithoutProblems().VisibleLeftBehindSections(stopped, decisions, world);

        Assert.Equal("Visible objects left behind: not performed because of an unexpected error.", sections[0].Lines[0]);
        Assert.Contains(
            "That step did not run, so visible objects whose surroundings were removed were not removed.",
            string.Join('\n', sections[^1].Lines),
            StringComparison.Ordinal);
    }

    private static void AssertLine(string line, CollectedObjects world, VisibleLeftBehindCheck evaluation, string? kind, string verdict)
    {
        var kindText = kind is null ? string.Empty : $" ({kind})";
        Assert.StartsWith($"  Visible left behind: {RecordNames.Describe(world.Targets[evaluation.TargetIndex])}{kindText} in ", line, StringComparison.Ordinal);
        Assert.Contains($": {verdict}", line, StringComparison.Ordinal);
        Assert.EndsWith($"; removed/total ground area {SectorAreasText.Describe(evaluation.Surroundings)}.", line, StringComparison.Ordinal);
    }

    /// <summary>An east direction cleared and a north direction cleared or kept.</summary>
    private static SectorAreas Areas(bool northCleared)
    {
        var tally = new SectorAreaTally(50);
        tally.Add(DirectionSector.East, 400f, removed: true);
        tally.Add(DirectionSector.North, 100f, removed: northCleared);
        return tally.Build();
    }

    private static (CollectedObjects World, VisibleLeftBehindResult Result, RemovalDecisions Decisions) Scene()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var world = new CollectedObjects(
            [.. targets],
            [],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
        VisibleLeftBehindCheck[] evaluations =
        [
            new(Removed, VisibleObjectKind.NpcsAndCreatures, Areas(northCleared: true), VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, null),
            new(Referenced, VisibleObjectKind.Containers, Areas(northCleared: true), VisibleLeftBehindOutcome.KeptReferenced, TestKeepReasons.Quest),
            new(ProtectedKind, VisibleObjectKind.Containers, Areas(northCleared: true), VisibleLeftBehindOutcome.KeptProtectedType, null),
            new(NearMiss, null, Areas(northCleared: false), VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, null),
            new(NoClearedDirection, null, new SectorAreaTally(50).Build(), VisibleLeftBehindOutcome.KeptSurroundingsMostlyKept, null),
            new(TooFewObjects, null, new SectorAreaTally(50).Build(), VisibleLeftBehindOutcome.KeptTooFewSurroundingObjects, null),
        ];
        var protection = ObjectsToKeep.Build(
            [.. targets],
            [TestTargets.Link(Removed, LinkedPartner)],
            TestTargets.References(TargetCount, new Dictionary<int, KeepReason> { [Referenced] = TestKeepReasons.Quest }),
            TestBaseFacts.Unresolved);
        var decisions = RemovalDecisions.Start(protection, TargetCount).Apply(
            RoundKind.VisibleLeftBehind,
            [
                new ProposedRemoval(new TargetId(Removed), new RemovalReason.VisibleObjectSurroundingsRemoved()),
                new ProposedRemoval(new TargetId(Referenced), new RemovalReason.VisibleObjectSurroundingsRemoved()),
            ]);
        return (world, new VisibleLeftBehindResult(StepOutcome.Performed, evaluations, [], Failure: null), decisions);
    }
}
