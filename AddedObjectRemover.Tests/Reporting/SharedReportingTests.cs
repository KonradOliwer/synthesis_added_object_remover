using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

public sealed class SharedReportingTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedTouching = 4;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;
    private static readonly TooCloseCause BySize = new TooCloseCause.ObjectSize();
    private static readonly TooCloseCause WithinDistance = new TooCloseCause.Distance(32f);

    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void Removals_ListRoundByRoundDirectRemovalsBeforeLinkedOnes()
    {
        var (world, decisions) = RunRounds();

        var removals = RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestLeftBehind.PerformedWithoutFindings);

        Assert.Equal(
            [
                (typeof(TooCloseRemoval), 0),
                (typeof(TouchingRemoval), 1),
                (typeof(LinkedRemoval), 2),
            ],
            removals.Select(removal => (removal.GetType(), removal.TargetIndex)));
        Assert.Equal(world.OtherModObjects[0], Assert.IsType<TooCloseRemoval>(removals[0]).TooCloseTo);
        Assert.Equal(1, Assert.IsType<LinkedRemoval>(removals[2]).LinkedToTargetIndex);
    }

    [Fact]
    public void Removals_NameTheChangedTerrainAndTheDistanceCauses()
    {
        var world = CreateWorld();
        var changedBy = new PluginName("Other.esp");
        var decisions = RemovalDecisions.Start(ObjectsToKeep.Build(world.Targets, [], TestTargets.References(TargetCount)), TargetCount)
            .Apply(RoundKind.ChangedTerrain, [Propose(0, new RemovalReason.TerrainChanged(changedBy, TerrainChangeKind.HeightAndTerrainType))])
            .Apply(RoundKind.TooClose, [Propose(1, new RemovalReason.TooClose(new OtherId(0), BySize)), Propose(2, new RemovalReason.TooClose(new OtherId(0), WithinDistance))]);

        var removals = RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestLeftBehind.PerformedWithoutFindings);

        var other = world.OtherModObjects[0];
        Assert.Equal(
            [new TerrainChangedRemoval(0, changedBy, TerrainChangeKind.HeightAndTerrainType), new TooCloseRemoval(1, other, BySize), new TooCloseRemoval(2, other, WithinDistance)],
            removals);
    }

    [Fact]
    public void TheSummaryCountsBothTooCloseCausesAsTooClose()
    {
        RemovedObject[] removals = [new TooCloseRemoval(0, CreateOtherModObject(), BySize), new TooCloseRemoval(1, CreateOtherModObject(), WithinDistance)];

        var summary = LogSections.BuildRemovalSummarySection(removals, StepOutcome.SwitchedOff);

        Assert.Equal(
            "Removed 2 objects (2 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects).",
            Assert.Single(summary.Lines));
    }

    [Theory]
    [InlineData(StepOutcome.Performed, 1, "Removed 2 objects (1 on changed terrain, 1 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects).")]
    [InlineData(StepOutcome.Performed, 0, "Removed 1 objects (0 on changed terrain, 1 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects).")]
    [InlineData(StepOutcome.SwitchedOff, 0, "Removed 1 objects (1 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects).")]
    [InlineData(StepOutcome.StoppedByUnexpectedError, 0, "Removed 1 objects (1 too close, 0 follow-up removals, 0 leftover invisible objects, 0 linked to removed objects).")]
    public void TheSummaryShowsTheChangedTerrainItemOnlyWhenTheStepWasPerformed(StepOutcome outcome, int onChangedTerrain, string expected)
    {
        List<RemovedObject> removals = [new TooCloseRemoval(0, CreateOtherModObject(), BySize)];
        if (onChangedTerrain > 0) removals.Add(new TerrainChangedRemoval(1, new PluginName("Other.esp"), TerrainChangeKind.Height));

        Assert.Equal(expected, Assert.Single(LogSections.BuildRemovalSummarySection(removals, outcome).Lines));
    }

    [Theory]
    [InlineData(TerrainChangeKind.Height, "terrain changed by Other.esp (height)")]
    [InlineData(TerrainChangeKind.TerrainType, "terrain changed by Other.esp (terrain type)")]
    [InlineData(TerrainChangeKind.HeightAndTerrainType, "terrain changed by Other.esp (height and terrain type)")]
    public void ATerrainChangeNamesTheModAndWhatChanged(TerrainChangeKind kind, string expected)
    {
        Assert.Equal(expected, Describe.RemovalReason(null!, new TerrainChangedRemoval(0, new PluginName("Other.esp"), kind)));
    }

    [Fact]
    public void ADistanceRemovalNamesTheDistanceAndTheOtherObject()
    {
        var other = CreateOtherModObject();

        var text = Describe.RemovalReason(null!, new TooCloseRemoval(0, other, WithinDistance));

        Assert.Equal($"within 32 units of {Describe.OtherObject(other)}", text);
    }

    [Fact]
    public void ASizeRemovalNamesTheOtherObjectWithoutADistance()
    {
        var other = CreateOtherModObject();

        Assert.Equal($"too close to {Describe.OtherObject(other)}", Describe.RemovalReason(null!, new TooCloseRemoval(0, other, BySize)));
    }

    [Fact]
    public void Removals_ComeInTheOrderTheStepNineResultGives()
    {
        var (world, decisions) = RunRounds();
        TargetId[] writeOrder = [new(2), new(0), new(1)];

        var removals = RemovalList.Removals(writeOrder, decisions, world, TestLeftBehind.PerformedWithoutFindings);

        Assert.Equal([2, 0, 1], removals.Select(removal => removal.TargetIndex));
    }

    [Fact]
    public void Kept_ListsHeldObjectsWithTheRemovedObjectTheyTouch()
    {
        var (_, decisions) = RunRounds();

        var kept = RemovalList.Kept(decisions);

        Assert.Equal([(Protected, (int?)null), (ProtectedTouching, 0)], kept.Select(entry => (entry.TargetIndex, entry.TouchedTargetIndex)));
    }

    [Fact]
    public void CountLinkedIn_CountsOnlyTheRoundsLinkedRemovals()
    {
        var (_, decisions) = RunRounds();

        Assert.Equal([0, 1], decisions.Rounds.Select(round => RemovalList.CountLinkedIn(decisions, round)));
    }

    [Fact]
    public void Hints_ReportRemovedUsedMarkersAndKeptForNonPlacedReferences()
    {
        var (world, decisions) = RunRounds();
        var tagged = world with
        {
            Targets =
            [
                .. world.Targets.Select((target, index) => target with
                {
                    Base = TestVisibility.TaggedBase(index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible),
                }),
            ],
        };
        var protection = CreateProtection(tagged.Targets);
        var decided = new StepResults(
            tagged, protection, ChangedTerrainResult.SwitchedOff, null!, TestLeftBehind.PerformedWithoutFindings, null!, decisions);

        var hints = ManualPatchHints.FindManualPatchHints(decided, TestVisibility.OfTaggedBasesOnly(), RemovalList.Removals(PatchWriter.ListRemovalsInWriteOrder(decided.Final), decided.Final, decided.Collected, decided.LeftBehind));

        Assert.Equal(
            [
                (ManualPatchHintType.RemovedMarker, 0),
                (ManualPatchHintType.KeptForNonPlacedReference, Protected),
                (ManualPatchHintType.KeptForNonPlacedReference, ProtectedTouching),
            ],
            hints.Select(hint => (hint.Type, hint.TargetIndex)));
    }

    [Fact]
    public void Kept_PrintsOneLinePerObject()
    {
        var (world, decisions) = RunRounds();
        var kept = RemovalList.Kept(decisions);

        var expected = new[]
        {
            $"  Kept {RecordNames.Describe(world.Targets[Protected])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            $"  Kept {RecordNames.Describe(world.Targets[ProtectedTouching])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail} "
                + $"(touches removed {RecordNames.Describe(world.Targets[0])}).",
        };
        Assert.Equal(expected, LogSections.BuildKeptLines(world, kept));
    }

    [Fact]
    public void LinkedRemovals_IsPrintedOnlyInTheDetailedLog()
    {
        Assert.Empty(LogSections.BuildLinkedRemovalLines(3, "follow-up", Normal));
        Assert.Equal(
            new[] { "Linked groups: 3 more objects removed with the follow-up removals they are linked to." },
            LogSections.BuildLinkedRemovalLines(3, "follow-up", Detailed).ToArray());
    }

    [Fact]
    public void MeshProblems_ShowTheMeshProblemsOnlyInTheDetailedLogAndTheMeshErrorsInBoth()
    {
        ImmutableArray<AssetProblem> problems = [AssetProblem.NotFound("m.nif")];
        var error = new UnexpectedError(
            "reading mesh triangles", "InvalidDataException: broke", "Objects using the mesh are treated as having no mesh triangles.", new ErrorSubject(SubjectKind.Mesh, "m.nif", Position: 0));

        var normal = LogSections.BuildMeshProblemsSection(problems, [error], Normal);
        var detailed = LogSections.BuildMeshProblemsSection(problems, [error], Detailed);

        Assert.Equal("meshProblems", normal.Id);
        Assert.Equal(UnexpectedErrorLines.Of([error], Normal).ToArray(), normal.Lines.ToArray());
        Assert.Equal(["  [mesh] not found: m.nif", ..UnexpectedErrorLines.Of([error], Detailed)], detailed.Lines.ToArray());
        Assert.Empty(LogSections.BuildMeshProblemsSection([], [], Detailed).Lines);
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 touches 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (CollectedObjects World, RemovalDecisions Decisions) RunRounds()
    {
        var world = CreateWorld();
        var decisions = RemovalDecisions.Start(CreateProtection(world.Targets), TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), BySize)), Propose(Protected, new RemovalReason.TooClose(new OtherId(0), BySize))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, new RemovalReason.Touching(new TargetId(0))), Propose(ProtectedTouching, new RemovalReason.Touching(new TargetId(0)))]);
        return (world, decisions);
    }

    private static CollectedObjects CreateWorld()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        return new CollectedObjects(
            [.. targets],
            [CreateOtherModObject()],
            [],
            false,
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace) },
            new ReadCounts(0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }

    private static ObjectsToKeep CreateProtection(IReadOnlyList<TargetObject> targets) =>
        ObjectsToKeep.Build(
            targets,
            [TestTargets.Link(1, 2)],
            TestTargets.References(
                TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedTouching] = QuestReason }));

    private static ProposedRemoval Propose(int index, RemovalReason reason) => new(new TargetId(index), reason);

    private static OtherObject CreateOtherModObject() =>
        new(
            new OtherId(0),
            new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x10).ToRecordKey(),
            TestTargets.Space,
            new PluginName("Other.esp"),
            EditorId: null,
            Base: null,
            Vector3.Zero,
            default(Vector3),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);
}
