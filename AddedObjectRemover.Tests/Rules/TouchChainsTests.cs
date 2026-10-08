using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The touch cascade's chains and depths, read from the removal decisions.</summary>
public class TouchChainsTests
{
    private const float TouchDistance = 1f;

    private static readonly RecordKey OtherSpace = TestTargets.SpaceKey(0x101);

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x711), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x712), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x713), @"test\plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-30.5f, -2, 0), new Vector3(30.5f, 2, 4))));

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "TouchChainsData", TableModel, ItemModel, PlankModel);

    [Fact]
    public void ObjectItsRoundFoundTouchingStaysInItsCausesChainWhenTheirPairTestsApart()
    {
        const int reached = 0;
        const int seed = 1;
        List<TargetObject> targets =
        [
            Place(reached, TableModel, TestTargets.Space, new Vector3(500, 0, 0)),
            Place(seed, TableModel, TestTargets.Space, Vector3.Zero),
        ];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Count));

        var components = TestTouchCascade.Execute(
            targets,
            Shapes,
            protection,
            [seed],
            TouchDistance,
            threads: 4,
            collectDiagnostics: true,
            (_, _) => new ScriptedRule([new ProposedRemoval(new TargetId(reached), new RemovalReason.Touching(new TargetId(seed)))])).TouchChains;

        Assert.Equal(1, components.Stats.Components);
        Assert.Equal(2, components.Stats.LargestComponent);
        Assert.Equal(new[] { seed, reached }, Assert.Single(components.Members).ToArray());
    }

    [Fact]
    public void LinkedRemovalWithALowerIndexThanItsPartnerIsOneStepBeyondIt()
    {
        const int partner = 0;
        const int seedTable = 1;
        const int itemOnTable = 2;
        List<TargetObject> targets =
        [
            Place(partner, TableModel, OtherSpace, new Vector3(3000, 0, 0)),
            Place(seedTable, TableModel, TestTargets.Space, Vector3.Zero),
            Place(itemOnTable, ItemModel, TestTargets.Space, new Vector3(5, 5, 10)),
        ];
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(itemOnTable, partner)], TestTargets.References(targets.Count));

        var run = TestTouchCascade.Execute(targets, Shapes, protection, [seedTable], TouchDistance, threads: 4, collectDiagnostics: true);
        var (decisions, components) = (run.RemovalDecisions, run.TouchChains);

        Assert.Equal(new RemovalReason.LinkedTo(new TargetId(itemOnTable)), decisions.Of(new TargetId(partner))!.Reason);
        Assert.Equal(1, components.DepthOf[itemOnTable]);
        Assert.Equal(2, components.DepthOf[partner]);
        Assert.Equal(2, components.Stats.MaxDepth);
    }

    [Fact]
    public void TwoSeedsJoinedThroughARemovedObjectFormOneChain()
    {
        const int firstSeed = 0;
        const int plank = 1;
        const int secondSeed = 2;
        List<TargetObject> targets =
        [
            Place(firstSeed, TableModel, TestTargets.Space, Vector3.Zero),
            Place(plank, PlankModel, TestTargets.Space, new Vector3(50, 0, 3)),
            Place(secondSeed, TableModel, TestTargets.Space, new Vector3(100, 0, 0)),
        ];
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Count));

        var run = TestTouchCascade.Execute(targets, Shapes, protection, [firstSeed, secondSeed], TouchDistance, threads: 4, collectDiagnostics: true);
        var (decisions, components) = (run.RemovalDecisions, run.TouchChains);

        Assert.True(decisions.IsRemoved(new TargetId(plank)));
        Assert.Equal(1, components.Stats.Components);
        Assert.Equal(3, components.Stats.LargestComponent);
        Assert.Equal(new[] { firstSeed, secondSeed, plank }, Assert.Single(components.Members).ToArray());
    }

    private static TargetObject Place(int index, TestStatic model, RecordKey space, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), model.Base, space);

    /// <summary>Proposes the given removals in the first round and nothing after, whatever the geometry says.</summary>
    private sealed class ScriptedRule(IReadOnlyList<ProposedRemoval> firstRound) : IRemovalRounds<RoundDetails>
    {
        private bool _proposed;

        public RoundProposals<RoundDetails> Next(IRemovalDecisions decisions, ImmutableArray<TargetId> removedLastRound)
        {
            var proposals = _proposed ? [] : firstRound;
            _proposed = true;
            return new RoundProposals<RoundDetails>(proposals, new TouchRoundDetails([.. proposals.Select(proposal => new IndexPair(((RemovalReason.Touching)proposal.Reason).Touched.Index, proposal.Target.Index))], PairTestStats.Zero));
        }
    }
}
