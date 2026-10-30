using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The also-remove follow-up passes: each pass decides from the decisions with every earlier round, the earlier passes' rounds
/// included; contacts are found once per target over all passes; the summary merges the passes; a failing pass ends the follow-ups
/// and keeps the passes before it.
/// </summary>
public class AlsoRemoveFollowUpTests
{
    private const int Seed = 1913;
    private const int Trials = 8;
    private const int SecondSeedCount = 3;
    private const int Workers = 8;
    private const float Threshold = 0.9f;
    private const string Failure = "broke";

    private const int BaseSlab = 0;
    private const int UpperSlab = 1;
    private const int SideSlab = 2;
    private const int Plank = 3;

    private const int RowStart = 0;
    private const int RowMiddle = 1;
    private const int RowEnd = 2;
    private const int OtherRowStart = 3;
    private const int OtherRowEnd = 4;

    private const int FirstSeed = 0;
    private const int KeptByFirstPass = 1;
    private const int SecondSeed = 2;
    private const int TouchedBySecondSeed = 3;
    private const int OnKeptObject = 4;

    private const string PlankMeshFile = "followup_plank.nif";

    private static readonly RemovalReason SecondSeedReason = new RemovalReason.VisibleObjectSurroundingsRemoved();

    private static readonly TestStatic SlabModel = new(
        new FormKey(TestTargets.TargetMod, 0x741), @"test\followup_slab.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x742), @"test\followup_plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-10, -3, 0), new Vector3(10, 3, 6))));

    private static readonly TestStatic CubeModel = new(
        new FormKey(TestTargets.TargetMod, 0x743), @"test\followup_cube.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10))));

    private static readonly IBaseObjectShapes SceneShapes = TestShapes.Create(TestTargets.TargetMod, "AlsoRemoveFollowUpData", SlabModel, PlankModel, CubeModel);

    /// <summary>
    /// The upper slab rests on the base slab; the plank lies half on the upper slab and half on the side slab, which stands apart
    /// from both slabs.
    /// </summary>
    private static readonly List<TargetObject> StackedSlabs =
    [
        TestTargets.Create(BaseSlab, TestTargets.At(Vector3.Zero), SlabModel.Base, TestTargets.Space),
        TestTargets.Create(UpperSlab, TestTargets.At(new Vector3(0, 0, 10)), SlabModel.Base, TestTargets.Space),
        TestTargets.Create(SideSlab, TestTargets.At(new Vector3(42, 0, 10)), SlabModel.Base, TestTargets.Space),
        TestTargets.Create(Plank, TestTargets.At(new Vector3(21, 0, 20)), PlankModel.Base, TestTargets.Space),
    ];

    /// <summary>Two rows of cubes, each cube touching the next one in its row, the rows far apart.</summary>
    private static readonly List<TargetObject> TwoRows =
    [
        TestTargets.Create(RowStart, TestTargets.At(Vector3.Zero), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(RowMiddle, TestTargets.At(new Vector3(10, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(RowEnd, TestTargets.At(new Vector3(20, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(OtherRowStart, TestTargets.At(new Vector3(100, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(OtherRowEnd, TestTargets.At(new Vector3(110, 0, 0)), CubeModel.Base, TestTargets.Space),
    ];

    /// <summary>
    /// Cubes: the first seed; the object touching it, which is referenced elsewhere; the second seed, touching that object too; an
    /// object touching only the second seed; and an object lying on the referenced one that touches neither seed.
    /// </summary>
    private static readonly List<TargetObject> KeptBetweenSeeds =
    [
        TestTargets.Create(FirstSeed, TestTargets.At(Vector3.Zero), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(KeptByFirstPass, TestTargets.At(new Vector3(10, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(SecondSeed, TestTargets.At(new Vector3(14, 10, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(TouchedBySecondSeed, TestTargets.At(new Vector3(14, 20, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(OnKeptObject, TestTargets.At(new Vector3(13, -3, 10)), CubeModel.Base, TestTargets.Space),
    ];

    /// <summary>A row of three cubes, and another row of a cube and a plank, the rows far apart.</summary>
    private static readonly List<TargetObject> MixedRows =
    [
        TestTargets.Create(RowStart, TestTargets.At(Vector3.Zero), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(RowMiddle, TestTargets.At(new Vector3(10, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(RowEnd, TestTargets.At(new Vector3(20, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(OtherRowStart, TestTargets.At(new Vector3(100, 0, 0)), CubeModel.Base, TestTargets.Space),
        TestTargets.Create(OtherRowEnd, TestTargets.At(new Vector3(110, 0, 0)), PlankModel.Base, TestTargets.Space),
    ];

    [Fact]
    public void TheSecondPassSeesTheFirstPasssRemovalsSoAnObjectHalfOnEachIsRemovedOnlyThen()
    {
        var scene = Scene.Of(StackedSlabs, SceneShapes, FollowUpRemovalMode.OnlyWhatLosesItsSupport, Threshold);
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([BaseSlab]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var afterFirst = seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds);
        var afterSideSlab = RemoveInNewRound(afterFirst, [SideSlab]);
        var second = followUp.FollowUp(afterSideSlab, NewestLeft(afterSideSlab));

        Assert.Equal([[UpperSlab], []], ProposedTargets(first));
        Assert.Equal([[Plank], []], ProposedTargets(second));
        Assert.All(scene.Find(RemoveInNewRound(scene.Seed([BaseSlab]), [SideSlab])).ProposedRounds, round => Assert.Empty(round));
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport)]
    public void EachPassProposesTheRoundsTheAlsoRemoveStepDecidesFromTheSameDecisionsAndSeeds(FollowUpRemovalMode mode)
    {
        var secondPassRemovals = 0;
        foreach (var (world, trial) in TestBlockWorlds.Create(Seed, Trials).Select((world, trial) => (world, trial)))
        {
            var scene = Scene.Of(world, mode);
            var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
            var seeded = scene.Seed(world.Seeds);

            var first = followUp.FollowUp(seeded, NewestLeft(seeded));
            var afterFirst = seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds);
            var afterSecondSeeds = RemoveInNewRound(afterFirst, PickUndecided(afterFirst, new Random(Seed + trial)));
            var second = followUp.FollowUp(afterSecondSeeds, NewestLeft(afterSecondSeeds));

            Assert.Equal(scene.Find(seeded).ProposedRounds.AsEnumerable(), first.ProposedRounds.AsEnumerable());
            Assert.Equal(scene.Find(afterSecondSeeds).ProposedRounds.AsEnumerable(), second.ProposedRounds.AsEnumerable());
            Assert.Null(second.Failure);
            secondPassRemovals += second.ProposedRounds.Sum(round => round.Count);
        }
        Assert.True(secondPassRemovals > 0, "No second pass proposed anything.");
    }

    [Fact]
    public void ATargetEvaluatedInTwoPassesHasItsContactsFoundOnce()
    {
        var scene = Scene.Of(StackedSlabs, SceneShapes, FollowUpRemovalMode.OnlyWhatLosesItsSupport, Threshold);
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([BaseSlab]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var readsAfterFirst = scene.Surfaces.Reads;
        var afterSideSlab = RemoveInNewRound(seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds), [SideSlab]);
        var second = followUp.FollowUp(afterSideSlab, NewestLeft(afterSideSlab));
        var summary = followUp.Summarize(afterSideSlab.ApplyRounds(RoundKind.AlsoRemove, second.ProposedRounds));

        Assert.True(readsAfterFirst > 0);
        Assert.Equal(readsAfterFirst, scene.Surfaces.Reads);
        Assert.Equal(2, EvaluatedTargets(summary).Count(target => target == Plank));
    }

    [Fact]
    public void TheSummaryOfTwoSupportPassesCountsRoundsOverBothPassesCandidatesOnceAndEveryEvaluation()
    {
        var scene = Scene.Of(StackedSlabs, SceneShapes, FollowUpRemovalMode.OnlyWhatLosesItsSupport, Threshold);
        var clock = new Clock();
        var followUp = scene.CreateFollowUp(clock.Read);
        var seeded = scene.Seed([BaseSlab]);

        var final = RunTwoPasses(followUp, seeded, [SideSlab]);
        var summary = followUp.Summarize(final);

        Assert.Equal(StepOutcome.Performed, summary.Outcome);
        Assert.True(summary.HadSeeds);
        Assert.Equal([new Round(1, RoundKind.TooClose), new Round(4, RoundKind.VisibleLeftBehind)], summary.SeedRounds.AsEnumerable());
        Assert.Equal([2, 3, 5, 6], summary.Rounds.Select(round => round.Number));
        Assert.All(summary.Rounds, round => Assert.Equal(RoundKind.AlsoRemove, round.Kind));
        Assert.Equal([[UpperSlab], [], [Plank], []], summary.ProposedRounds.Select(round => round.Select(proposal => proposal.Target.Index)));
        Assert.Equal(4, summary.Work.Rounds);
        Assert.Equal(2, summary.Work.Candidates);
        Assert.Equal(3, summary.Work.Evaluations);
        Assert.Equal(clock.TimePerPass * 2, summary.RoundsTime);
        Assert.Null(summary.Failure);
    }

    [Fact]
    public void ZeroWeightCandidatesOfASecondPassAreNumberedOnFromTheContactsTheFirstPassFound()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.OnlyWhatLosesItsSupport, Threshold, decorateSamples: samples => new ZeroWeightSurfaceSamples(samples));
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([OtherRowStart]);

        var summary = followUp.Summarize(RunTwoPasses(followUp, seeded, [RowMiddle]));

        var zeroWeights = summary.Evidence.Cast<SupportRoundDetails>().Select(round => round.ZeroWeights.Select(zero => (zero.EvaluatedBefore, zero.Diagnostic.TargetIndex)));
        Assert.Equal([[(0, OtherRowEnd)], [(1, RowStart), (2, RowEnd)]], zeroWeights);
    }

    [Fact]
    public void AnObjectKeptInThePreviousPassIsNeitherProposedAgainNorSpreadFromInTheNextPass()
    {
        var referenced = new Dictionary<int, KeepReason> { [KeptByFirstPass] = TestKeepReasons.Quest };
        var scene = Scene.Of(KeptBetweenSeeds, SceneShapes, FollowUpRemovalMode.EverythingTouchingIt, Threshold, referenced);
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([FirstSeed]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var afterFirst = seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds);
        var afterSecondSeed = RemoveInNewRound(afterFirst, [SecondSeed]);
        var second = followUp.FollowUp(afterSecondSeed, NewestLeft(afterSecondSeed));

        Assert.Equal([[KeptByFirstPass]], ProposedTargets(first));
        Assert.IsType<Decision.Kept>(afterFirst.Of(new TargetId(KeptByFirstPass)));
        Assert.Equal([[TouchedBySecondSeed], []], ProposedTargets(second));
    }

    [Fact]
    public void TheSummaryOfTwoTouchPassesFindsTheChainsOnceWithEachPasssSeedsAsRoots()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.EverythingTouchingIt, Threshold);
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([RowStart]);

        var final = RunTwoPasses(followUp, seeded, [OtherRowStart]);
        var summary = followUp.Summarize(final);

        Assert.Equal([2, 3, 4, 6, 7], summary.Rounds.Select(round => round.Number));
        Assert.Equal(5, summary.Work.Rounds);
        var chains = Assert.IsType<TouchChainSet>(summary.TouchChains);
        Assert.Equal([[RowStart, RowMiddle, RowEnd], [OtherRowStart, OtherRowEnd]], chains.Members.Select(members => members.AsEnumerable()));
        Assert.Equal([0, 0, 0, 1, 1], chains.ComponentOf.AsEnumerable());
        Assert.Equal([TouchChainSet.None, RowStart, RowMiddle, TouchChainSet.None, OtherRowStart], chains.ParentOf.AsEnumerable());
        Assert.Equal([0, 1, 2, 0, 1], chains.DepthOf.AsEnumerable());
        Assert.Equal(2, chains.Stats.Components);
        Assert.Equal(2, chains.Stats.ComponentsWithFollowUpRemovals);
        Assert.Equal(3, chains.Stats.LargestComponent);
        Assert.Equal(3, chains.Stats.Levels);
        Assert.Equal(2, chains.Stats.MaxDepth);
    }

    [Fact]
    public void AFailureInTheSecondPassProposesNothingEndsTheFollowUpsAndKeepsTheFirstPass()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.EverythingTouchingIt, Threshold);
        var clock = new Clock { FailingReading = Clock.ReadingsPerPass + 1 };
        var followUp = scene.CreateFollowUp(clock.Read);
        var seeded = scene.Seed([RowStart]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var afterFirst = seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds);
        var afterSecondSeeds = RemoveInNewRound(afterFirst, [OtherRowStart]);
        var second = followUp.FollowUp(afterSecondSeeds, NewestLeft(afterSecondSeeds));
        var third = followUp.FollowUp(afterSecondSeeds, NewestLeft(afterSecondSeeds));
        var summary = followUp.Summarize(afterSecondSeeds);

        Assert.Empty(second.ProposedRounds);
        Assert.Contains(Failure, second.Failure!.Failure);
        Assert.Empty(third.ProposedRounds);
        Assert.Equal(second.Failure, third.Failure);
        Assert.Equal(StepOutcome.Performed, summary.Outcome);
        Assert.Equal(second.Failure, summary.Failure);
        Assert.Equal([new Round(1, RoundKind.TooClose)], summary.SeedRounds.AsEnumerable());
        Assert.Equal([2, 3, 4], summary.Rounds.Select(round => round.Number));
        Assert.Equal([[RowMiddle], [RowEnd], []], summary.ProposedRounds.Select(round => round.Select(proposal => proposal.Target.Index)));
    }

    [Fact]
    public void AFailureWhileTheSecondPassReadsTheSurfaceOfAMeshKeepsTheFirstPass()
    {
        var scene = Scene.Of(MixedRows, SceneShapes, FollowUpRemovalMode.OnlyWhatLosesItsSupport, Threshold, decorateSamples: samples => new FailingPlankSurfaceSamples(samples));
        var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
        var seeded = scene.Seed([RowStart]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var afterSecondSeeds = RemoveInNewRound(seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds), [OtherRowStart]);
        var second = followUp.FollowUp(afterSecondSeeds, NewestLeft(afterSecondSeeds));
        var summary = followUp.Summarize(afterSecondSeeds);

        Assert.Null(first.Failure);
        Assert.NotEmpty(first.ProposedRounds);
        Assert.Empty(second.ProposedRounds);
        Assert.Contains(Failure, second.Failure!.Failure);
        Assert.Equal(StepOutcome.Performed, summary.Outcome);
        Assert.Equal(second.Failure, summary.Failure);
        Assert.Equal(first.ProposedRounds.AsEnumerable(), summary.ProposedRounds.AsEnumerable());
    }

    [Fact]
    public void AFailureInTheFirstPassStopsTheFollowUps()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.EverythingTouchingIt, Threshold);
        var followUp = scene.CreateFollowUp(new Clock { FailingReading = 1 }.Read);
        var seeded = scene.Seed([RowStart]);

        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var summary = followUp.Summarize(seeded);

        Assert.Empty(first.ProposedRounds);
        Assert.Equal(StepOutcome.StoppedByUnexpectedError, summary.Outcome);
        Assert.Equal(first.Failure, summary.Failure);
        Assert.Empty(summary.Rounds);
    }

    [Fact]
    public void ModeNothingProposesNothingAndIsSwitchedOff()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.Nothing, Threshold);
        var followUp = scene.CreateFollowUp(() => throw new InvalidOperationException(Failure));
        var seeded = scene.Seed([RowStart]);

        var pass = followUp.FollowUp(seeded, NewestLeft(seeded));

        Assert.Empty(pass.ProposedRounds);
        Assert.Null(pass.Failure);
        Assert.Equal(StepOutcome.SwitchedOff, followUp.Summarize(seeded).Outcome);
    }

    [Fact]
    public void WithoutSeedsNoRoundRunsAndTheSummaryHadNoSeeds()
    {
        var scene = Scene.Of(TwoRows, SceneShapes, FollowUpRemovalMode.EverythingTouchingIt, Threshold);
        var followUp = scene.CreateFollowUp(() => throw new InvalidOperationException(Failure));
        var seeded = scene.Seed([]);

        var pass = followUp.FollowUp(seeded, []);
        var summary = followUp.Summarize(seeded);

        Assert.Empty(pass.ProposedRounds);
        Assert.Equal(StepOutcome.Performed, summary.Outcome);
        Assert.False(summary.HadSeeds);
        Assert.Empty(summary.Rounds);
    }

    [Theory]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport)]
    public void TheSummaryOfOnePassIsTheResultOfTheAlsoRemoveStepWithoutReportFilesForTheSameSeeds(FollowUpRemovalMode mode)
    {
        foreach (var world in TestBlockWorlds.Create(Seed, Trials))
        {
            var scene = Scene.Of(world, mode);
            var followUp = scene.CreateFollowUp(() => TimeSpan.Zero);
            var seeded = scene.Seed(world.Seeds);

            var pass = followUp.FollowUp(seeded, NewestLeft(seeded));
            var summary = followUp.Summarize(seeded.ApplyRounds(RoundKind.AlsoRemove, pass.ProposedRounds));

            AssertSameResult(scene.Find(seeded), summary);
        }
    }

    private static RemovalDecisions RunTwoPasses(IAlsoRemoveFollowUp followUp, RemovalDecisions seeded, IReadOnlyList<int> secondSeeds)
    {
        var first = followUp.FollowUp(seeded, NewestLeft(seeded));
        var afterSecondSeeds = RemoveInNewRound(seeded.ApplyRounds(RoundKind.AlsoRemove, first.ProposedRounds), secondSeeds);
        var second = followUp.FollowUp(afterSecondSeeds, NewestLeft(afterSecondSeeds));
        return afterSecondSeeds.ApplyRounds(RoundKind.AlsoRemove, second.ProposedRounds);
    }

    private static void AssertSameResult(RestingObjectsResult expected, RestingObjectsResult actual)
    {
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Mode, actual.Mode);
        Assert.Equal(expected.HadSeeds, actual.HadSeeds);
        Assert.Equal(expected.SeedRounds.AsEnumerable(), actual.SeedRounds.AsEnumerable());
        Assert.Equal(expected.Rounds.AsEnumerable(), actual.Rounds.AsEnumerable());
        Assert.Equal(expected.ProposedRounds.AsEnumerable(), actual.ProposedRounds.AsEnumerable());
        Assert.Equal(expected.Evidence.Select(Describe), actual.Evidence.Select(Describe));
        Assert.Equal(expected.Work, actual.Work);
        Assert.Equal(expected.RoundsTime, actual.RoundsTime);
        Assert.Equal(expected.Failure, actual.Failure);
        Assert.Equal(expected.TouchChainsFailure, actual.TouchChainsFailure);
        Assert.Equal(expected.ReportFileDetails, actual.ReportFileDetails);
        Assert.Equal(expected.TouchChains is null, actual.TouchChains is null);
        if (expected.TouchChains is not { } chains) return;

        Assert.Equal(chains.Stats, actual.TouchChains!.Stats);
        Assert.Equal(chains.ComponentOf.AsEnumerable(), actual.TouchChains.ComponentOf.AsEnumerable());
        Assert.Equal(chains.ParentOf.AsEnumerable(), actual.TouchChains.ParentOf.AsEnumerable());
        Assert.Equal(chains.DepthOf.AsEnumerable(), actual.TouchChains.DepthOf.AsEnumerable());
        Assert.Equal(chains.Members.Select(members => members.AsEnumerable()), actual.TouchChains.Members.Select(members => members.AsEnumerable()));
    }

    private static string Describe(RoundDetails round) => round switch
    {
        TouchRoundDetails touch => $"{string.Join(";", touch.Reached)} | {touch.Work}",
        SupportRoundDetails support =>
            $"{string.Join(";", support.Evaluations.Select(Describe))} | {support.Work} | {string.Join(";", support.ZeroWeights.Select(zero => $"{zero.EvaluatedBefore}:{zero.Diagnostic.TargetIndex}"))}",
        _ => throw new ArgumentException($"Unknown round details {round.GetType().Name}.", nameof(round)),
    };

    private static string Describe(SupportEvaluation evaluation) =>
        $"{evaluation.TargetIndex}:{evaluation.Proposed}:{evaluation.Contacts.ContactPoints}:{evaluation.Contacts.TotalWeight}:{string.Join(",", evaluation.Shares)}";

    private static IEnumerable<int> EvaluatedTargets(RestingObjectsResult result) =>
        result.Evidence.Cast<SupportRoundDetails>().SelectMany(round => round.Evaluations).Select(evaluation => evaluation.TargetIndex);

    private static IEnumerable<IEnumerable<int>> ProposedTargets(FollowUpPass pass) => ProposedTargets(pass.ProposedRounds);

    private static IEnumerable<IEnumerable<int>> ProposedTargets(RestingObjectsResult result) => ProposedTargets(result.ProposedRounds);

    private static IEnumerable<IEnumerable<int>> ProposedTargets(ImmutableArray<IReadOnlyList<ProposedRemoval>> rounds) =>
        rounds.Select(round => round.Select(proposal => proposal.Target.Index));

    private static ImmutableArray<TargetId> NewestLeft(IRemovalDecisions decisions) => decisions.LeftIn(decisions.Rounds[^1]);

    private static RemovalDecisions RemoveInNewRound(RemovalDecisions decisions, IReadOnlyList<int> targets) =>
        decisions.Apply(RoundKind.VisibleLeftBehind, [.. targets.Select(target => new ProposedRemoval(new TargetId(target), SecondSeedReason))]);

    private static List<int> PickUndecided(IRemovalDecisions decisions, Random random) =>
        [.. Enumerable.Range(0, TestBlockWorlds.TargetCount)
            .Where(target => !decisions.IsDecided(new TargetId(target)))
            .OrderBy(_ => random.Next())
            .Take(SecondSeedCount)
            .Order()];

    /// <summary>The targets and caches of one test, with the surface samples counting their reads.</summary>
    private sealed class Scene
    {
        private readonly List<TargetObject> _targets;
        private readonly ObjectsToKeep _protection;
        private readonly IBaseObjectShapes _shapes;
        private readonly TriangleStore _triangles;
        private readonly IVisibleObjectsOfAnyPlugin _objectsOfAnyPlugin;
        private readonly AlsoRemoveSettings _settings;
        private readonly Execution _execution = new(Workers);

        private Scene(
            List<TargetObject> targets,
            ObjectsToKeep protection,
            IBaseObjectShapes shapes,
            FollowUpRemovalMode mode,
            float threshold,
            Func<ISurfaceSamples, ISurfaceSamples>? decorateSamples)
        {
            _targets = targets;
            _protection = protection;
            _shapes = shapes;
            _triangles = new TriangleStore(shapes.ReadTriangles);
            var samples = TestSurfaceSamples.Over(_triangles);
            Surfaces = new CountedSurfaceSamples(decorateSamples is null ? samples : decorateSamples(samples));
            _objectsOfAnyPlugin = TestScenes.CreateWithSupportOnlyObjects(targets, [], shapes).VisibleObjectsOfAnyPlugin();
            _settings = new AlsoRemoveSettings(mode, TestBlockWorlds.TouchDistance, threshold, TouchChainsShown: true);
        }

        public CountedSurfaceSamples Surfaces { get; }

        /// <param name="referenced">Objects referenced elsewhere, which are kept when proposed.</param>
        /// <param name="decorateSamples">Wraps the surface samples the follow-up reads.</param>
        public static Scene Of(
            List<TargetObject> targets,
            IBaseObjectShapes shapes,
            FollowUpRemovalMode mode,
            float threshold,
            IReadOnlyDictionary<int, KeepReason>? referenced = null,
            Func<ISurfaceSamples, ISurfaceSamples>? decorateSamples = null) =>
            new(targets, ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Count, referenced), TestBaseFacts.Unresolved), shapes, mode, threshold, decorateSamples);

        public static Scene Of(TestBlockWorlds.World world, FollowUpRemovalMode mode) =>
            new(world.Targets, world.Protection, TestBlockWorlds.Shapes, mode, TestBlockWorlds.SupportThreshold, decorateSamples: null);

        public RemovalDecisions Seed(IReadOnlyList<int> seeds) => TestSeededDecisions.Seed(_protection, _targets.Count, seeds);

        public IAlsoRemoveFollowUp CreateFollowUp(Func<TimeSpan> clock) =>
            RestingObjects.CreateFollowUp([.. _targets], _settings, _execution, clock, _shapes, _triangles, Surfaces, TestGround.NoTerrain(), () => _objectsOfAnyPlugin);

        /// <summary>The also-remove step seeded by the newest round of <paramref name="decisions"/>, without report files.</summary>
        public RestingObjectsResult Find(RemovalDecisions decisions) =>
            RestingObjects.Find(
                new RestingObjectsInput([.. _targets], decisions, [decisions.Rounds[^1]], _settings, _execution, () => TimeSpan.Zero, StepOutcome.SwitchedOff),
                TestBaseFacts.Unresolved,
                _shapes,
                _triangles,
                new CountedSurfaceSamples(TestSurfaceSamples.Over(_triangles)),
                TestGround.NoTerrain(),
                () => _objectsOfAnyPlugin);
    }

    private sealed class CountedSurfaceSamples(ISurfaceSamples inner) : ISurfaceSamples
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public SampledSurface? Of(string meshPath)
        {
            Interlocked.Increment(ref _reads);
            return inner.Of(meshPath);
        }
    }

    /// <summary>Samples whose bounds are so small that every sample has no weight, so the contacts of every mesh have contact points but no support weight.</summary>
    private sealed class ZeroWeightSurfaceSamples(ISurfaceSamples inner) : ISurfaceSamples
    {
        private const float TinyExtent = 1e-20f;

        public SampledSurface? Of(string meshPath) => inner.Of(meshPath) is { } surface ? surface with { Bounds = new Box(Vector3.Zero, new Vector3(TinyExtent)) } : null;
    }

    /// <summary>Samples that throw when the plank mesh is read.</summary>
    private sealed class FailingPlankSurfaceSamples(ISurfaceSamples inner) : ISurfaceSamples
    {
        public SampledSurface? Of(string meshPath) =>
            meshPath.EndsWith(PlankMeshFile, StringComparison.OrdinalIgnoreCase) ? throw new InvalidOperationException(Failure) : inner.Of(meshPath);
    }

    /// <summary>A clock one second further on each reading, which throws at the failing reading.</summary>
    private sealed class Clock
    {
        /// <summary>A pass reads the clock when its rounds start and when they end.</summary>
        public const int ReadingsPerPass = 2;

        private int _readings;

        public TimeSpan TimePerPass { get; } = TimeSpan.FromSeconds(ReadingsPerPass - 1);

        /// <summary>The reading that throws; none when zero.</summary>
        public int FailingReading { get; init; }

        public TimeSpan Read()
        {
            _readings++;
            if (_readings == FailingReading) throw new InvalidOperationException(Failure);
            return TimeSpan.FromSeconds(_readings);
        }
    }
}
