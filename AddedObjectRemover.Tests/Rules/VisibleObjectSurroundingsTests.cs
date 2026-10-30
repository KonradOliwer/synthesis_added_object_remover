using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The surroundings of the visible left-behind step's candidates across rounds. Random worlds of visible objects, some restored
/// instead of disabled, some referenced, some linked and some kept shrunk or turned, go through random rounds of both kinds that the
/// updates see in different splits; every result is compared with a full measurement under the newest decisions.
/// </summary>
public class VisibleObjectSurroundingsTests
{
    private const int Seed = 2026;
    private const int Trials = 12;
    private const int TargetCount = 70;
    private const int LinkCount = 6;
    private const int RestoredOneIn = 3;
    private const int ReferencedOneIn = 8;
    private const int ProposedOneIn = 9;
    private const int FitCount = 4;
    private const int Updates = 6;
    private const int MostRoundsPerUpdate = 3;
    private const float Reach = 500f;
    private const float SmallestScale = 0.5f;
    private const float LargestScale = 2f;
    private const float LookAround = 260f;
    private const float Spacing = 100f;
    private const int DirectionClearedPercent = 80;
    private const int DefaultWorkers = 4;

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");
    private static readonly TestStatic Block = new(new FormKey(OtherMod, 0x801), @"test\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20), new Vector3(20))));
    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(OtherMod, "VisibleObjectSurroundingsData", Block));
    private static readonly RemovalReason AnyReason = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    [Fact]
    public void InitiallyEveryCandidateIsMeasuredWithEveryOtherObjectStanding()
    {
        foreach (var world in RandomWorlds())
        {
            var surroundings = Measure(world, DefaultWorkers);

            Assert.Equal(world.Candidates, surroundings.Initial.Select(measured => measured.TargetIndex));
            foreach (var measured in surroundings.Initial)
                Assert.Equal(Fingerprint(MeasureFully(world, measured.TargetIndex, world.Unjudged)), Fingerprint(measured.Surroundings));
        }
    }

    [Fact]
    public void AfterEveryUpdateEachUndecidedCandidateEqualsAFullMeasurementBitForBit()
    {
        var fittedThatLeft = 0;
        foreach (var (world, trialSeed) in RandomWorlds().Select((world, trial) => (world, Seed + trial)))
        {
            var (initial, updates) = Play(world, trialSeed, DefaultWorkers);

            var latest = initial.ToDictionary(measured => measured.TargetIndex, measured => measured.Surroundings);
            foreach (var update in updates)
            {
                foreach (var measured in update.Changed) latest[measured.TargetIndex] = measured.Surroundings;
                foreach (var (candidate, areas) in latest.Where(entry => !update.View.IsDecided(new TargetId(entry.Key))))
                    Assert.Equal(Fingerprint(MeasureFully(world, candidate, update.View)), Fingerprint(areas));
            }
            fittedThatLeft += world.Start.All().Count(entry => entry.Decision is Decision.Fitted && updates[^1].View.HasLeftItsPlace(entry.Target));
        }
        Assert.True(fittedThatLeft > 0, "No fitted object left its place in an also-remove round.");
    }

    [Fact]
    public void AnUpdateReturnsExactlyTheUndecidedCandidatesWithANewlyGoneNeighbourInTargetOrder()
    {
        foreach (var (world, trialSeed) in RandomWorlds().Select((world, trial) => (world, Seed + trial)))
        {
            foreach (var update in Play(world, trialSeed, DefaultWorkers).Updates)
            {
                var newlyGone = update.View.Rounds.Skip(update.RoundsBefore).SelectMany(round => update.View.LeftIn(round)).Select(id => id.Index).ToHashSet();
                var expected = world.Candidates.Where(candidate =>
                    !update.View.IsDecided(new TargetId(candidate)) && NeighboursOf(world, candidate).Any(newlyGone.Contains));

                Assert.Equal(expected, update.Changed.Select(measured => measured.TargetIndex));
            }
        }
    }

    [Fact]
    public void AnUpdateWithoutNewRoundsReturnsNothing()
    {
        var world = RandomWorlds().First();
        var surroundings = Measure(world, DefaultWorkers);
        Assert.NotEmpty(surroundings.Update(world.Start));

        Assert.Empty(surroundings.Update(world.Start));
    }

    /// <summary>Four objects in a row; the round removes the first two and keeps the referenced last one, so only the third is measured again.</summary>
    [Fact]
    public void CandidatesTheRoundsDecidedAreNotMeasuredAgain()
    {
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, 4).Select(index => Visible(index, new Vector3(index * Spacing, 0, 0), angle: 0, scale: 1))];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length, new Dictionary<int, KeepReason> { [3] = TestKeepReasons.Quest }), TestBaseFacts.Unresolved);
        var unjudged = TestSeededDecisions.StartDisablingAll(protection, targets.Length);
        var world = new World(targets, TestScenes.Create(targets, [], Shapes).VisibleTargets, unjudged, unjudged);
        var surroundings = Measure(world, DefaultWorkers);

        var changed = surroundings.Update(unjudged.Apply(RoundKind.VisibleLeftBehind, Propose(0, 1, 3)));

        Assert.Equal(new[] { 2 }, changed.Select(measured => measured.TargetIndex));
        Assert.Equal(SectorState.Removed, changed[0].Surroundings.State(DirectionSector.West));
        Assert.Equal(SectorState.Kept, changed[0].Surroundings.State(DirectionSector.East));
    }

    [Fact]
    public void AnObjectLeavingItsPlaceAgainIsABug()
    {
        ImmutableArray<TargetObject> targets = [Visible(0, Vector3.Zero, angle: 0, scale: 1), Visible(1, new Vector3(Spacing, 0, 0), angle: 0, scale: 1)];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved);
        RemovalDecisions Unjudged() => TestSeededDecisions.StartDisablingAll(protection, targets.Length);
        var world = new World(targets, TestScenes.Create(targets, [], Shapes).VisibleTargets, Unjudged(), Unjudged());
        var surroundings = Measure(world, DefaultWorkers);
        surroundings.Update(Unjudged().Apply(RoundKind.TooClose, Propose(0)));

        var otherDecisions = Unjudged().Apply(RoundKind.TooClose, Propose(1)).Apply(RoundKind.VisibleLeftBehind, Propose(0));

        Assert.Throws<InvalidOperationException>(() => surroundings.Update(otherDecisions));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void TheResultsDoNotDependOnTheNumberOfWorkers(int workers)
    {
        foreach (var (world, trialSeed) in RandomWorlds().Select((world, trial) => (world, Seed + trial)))
            Assert.Equal(Describe(Play(world, trialSeed, workers: 1)), Describe(Play(world, trialSeed, workers)));
    }

    /// <param name="Unjudged">The decisions before any round.</param>
    /// <param name="Start">The decisions after the earlier steps' round; its undecided objects are the candidates.</param>
    private sealed record World(ImmutableArray<TargetObject> Targets, IVisibleTargetObjects Index, RemovalDecisions Unjudged, RemovalDecisions Start)
    {
        public ImmutableArray<int> Candidates { get; } = [.. Enumerable.Range(0, Targets.Length).Where(index => !Start.IsDecided(new TargetId(index)))];
    }

    /// <param name="RoundsBefore">The rounds the update before saw.</param>
    private sealed record UpdateStep(IRemovalDecisions View, int RoundsBefore, IReadOnlyList<MeasuredSurroundings> Changed);

    private static IEnumerable<World> RandomWorlds() => Enumerable.Range(0, Trials).Select(trial => RandomWorld(new Random(Seed + trial)));

    private static World RandomWorld(Random random)
    {
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, TargetCount).Select(index => RandomTarget(random, index))];
        var referenced = Enumerable.Range(0, TargetCount).Where(_ => random.Next(ReferencedOneIn) == 0).ToDictionary(index => index, _ => TestKeepReasons.Quest);
        TargetLink[] links = [.. Enumerable.Range(0, LinkCount).Select(_ => random.Next(TargetCount - 1)).Select(from => TestTargets.Link(from, from + 1))];
        var protection = ObjectsToKeep.Build(targets, links, TestTargets.References(TargetCount, referenced), TestBaseFacts.Unresolved);
        var unjudged = RemovalDecisions.Start(protection, [.. targets.Select(target => random.Next(RestoredOneIn) == 0 ? Restore(target) : new WhenRemoved.Disable())]);
        var fitted = unjudged.ApplyFits(FitRandomly(random, targets, protection));
        var start = fitted.Apply(RoundKind.TooClose, ProposeRandomly(random, fitted, RoundKind.TooClose));
        return new World(targets, TestScenes.Create(targets, [], Shapes).VisibleTargets, unjudged, start);
    }

    /// <summary>A few unprotected, unlinked objects kept where they stand, so the index measures them where the fits put them.</summary>
    private static ProposedFit[] FitRandomly(Random random, ImmutableArray<TargetObject> targets, ObjectsToKeep protection) =>
    [
        .. targets.Where(target => !protection.IsProtected(target.Id.Index) && !protection.Groups.IsLinked(target.Id.Index))
            .OrderBy(_ => random.Next())
            .Take(FitCount)
            .Select(target => new ProposedFit(target.Id, AnyReason, target.Placement)),
    ];

    private static TargetObject RandomTarget(Random random, int index) =>
        Visible(
            index,
            new Vector3(RandomOffset(random), RandomOffset(random), RandomOffset(random) / 10),
            random.NextSingle() * MathF.Tau,
            SmallestScale + random.NextSingle() * (LargestScale - SmallestScale));

    private static float RandomOffset(Random random) => (random.NextSingle() * 2 - 1) * Reach;

    private static TargetObject Visible(int index, Vector3 position, float angle, float scale) =>
        TestTargets.Create(index, TestTargets.At(position, angle, scale), Block.Base, TestTargets.Space);

    private static WhenRemoved Restore(TargetObject target) =>
        new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), target.Placement));

    /// <summary>
    /// The first update sees the earlier steps' round; each later one sees one to a few new rounds of random kinds, decided on a
    /// working copy so a world can be played again.
    /// </summary>
    private static (IReadOnlyList<MeasuredSurroundings> Initial, List<UpdateStep> Updates) Play(World world, int seed, int workers)
    {
        var random = new Random(seed);
        var copy = world.Start.WorkingCopy();
        var surroundings = Measure(world, workers);
        var updates = new List<UpdateStep> { new(world.Start, RoundsBefore: 0, surroundings.Update(world.Start)) };
        for (var update = 1; update <= Updates; update++)
        {
            var before = updates[^1].View;
            var view = Enumerable.Range(0, random.Next(1, MostRoundsPerUpdate + 1))
                .Aggregate(before, (decisions, _) => DecideRandomRound(random, copy, decisions));
            updates.Add(new UpdateStep(view, before.Rounds.Length, surroundings.Update(view)));
        }
        return (surroundings.Initial, updates);
    }

    private static IRemovalDecisions DecideRandomRound(Random random, IRemovalDecisionsCopy copy, IRemovalDecisions decisions)
    {
        var kind = RandomKind(random);
        return copy.DecideRound(kind, ProposeRandomly(random, decisions, kind));
    }

    private static RoundKind RandomKind(Random random) => random.Next(2) == 0 ? RoundKind.VisibleLeftBehind : RoundKind.AlsoRemove;

    /// <remarks>Only an also-remove round may remove a fitted object.</remarks>
    private static ProposedRemoval[] ProposeRandomly(Random random, IRemovalDecisions decisions, RoundKind kind) =>
        Propose([.. Enumerable.Range(0, TargetCount).Where(index => MayBeProposed(decisions, new TargetId(index), kind) && random.Next(ProposedOneIn) == 0)]);

    private static bool MayBeProposed(IRemovalDecisions decisions, TargetId target, RoundKind kind) =>
        kind == RoundKind.AlsoRemove ? decisions.MayStillBeRemoved(target) : !decisions.IsDecided(target);

    private static ProposedRemoval[] Propose(params int[] targets) => [.. targets.Select(index => new ProposedRemoval(new TargetId(index), AnyReason))];

    private static VisibleObjectSurroundings Measure(World world, int workers) =>
        VisibleObjectSurroundings.Measure(
            world.Targets, world.Candidates, world.Index, LookAround, DirectionClearedPercent, TargetWorkOrder.Of(world.Targets), new Execution(workers));

    private static SectorAreas MeasureFully(World world, int candidate, IRemovalDecisions decisions)
    {
        var target = world.Targets[candidate];
        return SectorAreaTally.Measure(
            AroundOf(world, target).Select(neighbour => (neighbour.Box, NeighbourFates.Of(decisions, neighbour.Target))),
            target.Transform.Position,
            DirectionClearedPercent);
    }

    private static IEnumerable<int> NeighboursOf(World world, int candidate) => AroundOf(world, world.Targets[candidate]).Select(neighbour => neighbour.Target.Index);

    private static IEnumerable<VisibleTargetNear> AroundOf(World world, TargetObject target)
    {
        var neighbours = new List<VisibleTargetNear>();
        world.Index.Around(target.SpaceKey, target.Transform.Position, LookAround, neighbours);
        return neighbours.Where(neighbour => neighbour.Target != target.Id);
    }

    private static IEnumerable<string> Describe((IReadOnlyList<MeasuredSurroundings> Initial, List<UpdateStep> Updates) played) =>
        played.Updates.Select(update => update.Changed).Prepend(played.Initial)
            .Select(measured => string.Join(';', measured.Select(entry => $"{entry.TargetIndex}:{Fingerprint(entry.Surroundings)}")));

    private static string Fingerprint(SectorAreas areas) =>
        $"{Bits(areas.TotalAreas)}|{Bits(areas.RemovedAreas)}|{Bits(areas.RestoredAreas)}|{string.Join(',', areas.States)}";

    private static string Bits(IEnumerable<float> values) => string.Join(',', values.Select(value => BitConverter.SingleToInt32Bits(value).ToString("X8")));
}
