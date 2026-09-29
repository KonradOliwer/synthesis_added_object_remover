using System.Collections.Immutable;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Tests.Fixtures;
using P3Float = Noggog.P3Float;

namespace AddedObjectRemover.Tests.Reporting;

public sealed class SharedReportingTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedTouching = 4;

    private static readonly KeepReason QuestReason = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST");
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void Removals_ListRoundByRoundDirectRemovalsBeforeLinkedOnes()
    {
        var (world, ledger) = RunRounds();

        var removals = Decisions.Removals(ledger, world, LeftoverResult.None);

        Assert.Equal(
            [
                (typeof(TooCloseRemoval), 0),
                (typeof(TouchingRemoval), 1),
                (typeof(LinkedRemoval), 2),
            ],
            removals.Select(removal => (removal.GetType(), removal.TargetIndex)));
        Assert.Equal(world.Rivals[0], Assert.IsType<TooCloseRemoval>(removals[0]).TooCloseTo);
        Assert.Equal(1, Assert.IsType<LinkedRemoval>(removals[2]).LinkedToTargetIndex);
    }

    [Fact]
    public void Kept_ListsHeldObjectsWithTheRemovedObjectTheyTouch()
    {
        var (_, ledger) = RunRounds();

        var kept = Decisions.Kept(ledger);

        Assert.Equal([(Protected, (int?)null), (ProtectedTouching, 0)], kept.Select(entry => (entry.TargetIndex, entry.TouchedTargetIndex)));
    }

    [Fact]
    public void CountLinkedIn_CountsOnlyTheRoundsLinkedRemovals()
    {
        var (_, ledger) = RunRounds();

        Assert.Equal([0, 1], ledger.Rounds.Select(round => Decisions.CountLinkedIn(ledger, round)));
    }

    [Fact]
    public void Hints_ReportRemovedUsedMarkersAndKeptForNonPlacedReferences()
    {
        var (world, ledger) = RunRounds();
        var looks = new TargetLooks([.. Enumerable.Range(0, TargetCount).Select(index =>
            index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible)]);
        var protection = Protection.Build(world.Targets, world.Links, world.References);
        var decided = new Decided(
            world, looks, null!, null!, protection, null!, null!, null!, null, LeftoverResult.None, null!, ledger);

        var hints = ManualPatchHints.Hints(decided, Decisions.Removals(decided.Final, decided.World, decided.Leftovers));

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
        var (world, ledger) = RunRounds();
        var kept = Decisions.Kept(ledger);

        var expected = new[]
        {
            $"  Kept {RecordNames.Describe(world.Targets[Protected])} in {world.SpaceNames[TestTargets.Space]}: linked from QUST.",
            $"  Kept {RecordNames.Describe(world.Targets[ProtectedTouching])} in {world.SpaceNames[TestTargets.Space]}: linked from QUST "
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
    public void Problems_ShowArchiveProblemsAlwaysAndMeshProblemsOnlyInTheDetailedLog()
    {
        var problems = new PhaseProblems(
            [new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "Warning: archive a.bsa is unreadable.")],
            [new AssetProblem("m.nif", AssetProblemKind.NotFound, "[mesh] m.nif not found.")]);

        Assert.Equal(new[] { "Warning: archive a.bsa is unreadable." }, LogSections.Problems(problems, Normal).ToArray());
        Assert.Equal(new[] { "Warning: archive a.bsa is unreadable.", "[mesh] m.nif not found." }, LogSections.Problems(problems, Detailed).ToArray());
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 touches 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (World World, Ledger Ledger) RunRounds()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var links = new[] { TestTargets.Link(1, 2) };
        var references = TestTargets.References(
            TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedTouching] = QuestReason });
        var world = new World(
            [.. targets],
            [CreateRival()],
            Collected<ImmutableArray<OtherObject>>.NotCollected,
            [.. links],
            [.. references],
            new Dictionary<FormKey, string> { [TestTargets.Space] = "Tamriel" },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0),
            []);
        var ledger = Ledger.Start(Protection.Build(world.Targets, world.Links, world.References), TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new Cause.TooClose(new OtherId(0))), Propose(Protected, new Cause.TooClose(new OtherId(0)))])
            .Apply(RoundKind.FollowUp, [Propose(1, new Cause.Touching(new TargetId(0))), Propose(ProtectedTouching, new Cause.Touching(new TargetId(0)))]);
        return (world, ledger);
    }

    private static Proposal Propose(int index, Cause cause) => new(new TargetId(index), cause);

    private static OtherObject CreateRival() =>
        new(
            new OtherId(0),
            new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x10),
            TestTargets.Space,
            ModKey.FromNameAndExtension("Other.esp"),
            EditorId: null,
            Base: null,
            Vector3.Zero,
            default(P3Float),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);
}
