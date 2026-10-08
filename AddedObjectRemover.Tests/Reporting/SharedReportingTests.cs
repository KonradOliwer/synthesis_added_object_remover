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
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void Removals_ListRoundByRoundDirectRemovalsBeforeLinkedOnes()
    {
        var (world, decisions) = RunRounds();

        var removals = RemovalList.Removals(decisions, world, LeftBehindResult.None);

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
            tagged, null!, null!, protection, null!, null!, null, LeftBehindResult.None, null!, decisions);

        var hints = ManualPatchHints.Hints(decided, TestVisibility.OfTaggedBasesOnly(), RemovalList.Removals(decided.Final, decided.World, decided.LeftBehind));

        Assert.Equal(
            [
                (ManualPatchHintType.RemovedMarker, 0),
                (ManualPatchHintType.KeptForNonPlacedReference, Protected),
                (ManualPatchHintType.KeptForNonPlacedReference, ProtectedTouching),
            ],
            hints.Select(hint => (hint.Type, hint.TargetIndex)));
    }

    [Fact]
    public void Kept_PrintsOneLinePerObjectInBothLogModes()
    {
        var (world, decisions) = RunRounds();
        var kept = RemovalList.Kept(decisions);

        var expected = new[]
        {
            $"  Kept {RecordNames.Describe(world.Targets[Protected])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            $"  Kept {RecordNames.Describe(world.Targets[ProtectedTouching])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail} "
                + $"(touches removed {RecordNames.Describe(world.Targets[0])}).",
        };
        Assert.Equal(expected, LogSections.Kept(world, kept, Normal));
        Assert.Equal(expected, LogSections.Kept(world, kept, Detailed));
    }

    [Fact]
    public void LinkedRemovals_IsPrintedOnlyInTheDetailedLog()
    {
        Assert.Empty(LogSections.LinkedRemovals(3, "follow-up", Normal));
        Assert.Equal(
            new[] { "Linked groups: 3 more objects removed with the follow-up removals they are linked to." },
            LogSections.LinkedRemovals(3, "follow-up", Detailed).ToArray());
    }

    [Fact]
    public void Problems_ShowMeshProblemsOnlyInTheDetailedLog()
    {
        var problems = new PhaseProblems([new AssetProblem("m.nif", AssetProblemKind.NotFound, "[mesh] m.nif not found.")]);

        Assert.Empty(LogSections.Problems(problems, Normal));
        Assert.Equal(new[] { "[mesh] m.nif not found." }, LogSections.Problems(problems, Detailed).ToArray());
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 touches 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (CollectedObjects World, RemovalDecisions Decisions) RunRounds()
    {
        var world = new CollectedObjects(
            [.. TestTargets.CreateMany(TargetCount)],
            [CreateOtherModObject()],
            null,
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace) },
            new ReadCounts(0, 0, 0, 0, 0, 0),
            []);
        var decisions = RemovalDecisions.Start(CreateProtection(world.Targets), TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0))), Propose(Protected, new RemovalReason.TooClose(new OtherId(0)))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, new RemovalReason.Touching(new TargetId(0))), Propose(ProtectedTouching, new RemovalReason.Touching(new TargetId(0)))]);
        return (world, decisions);
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
