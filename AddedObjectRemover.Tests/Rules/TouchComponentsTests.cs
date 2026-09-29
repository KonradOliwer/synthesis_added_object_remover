using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The touch cascade's components and depths, read from the ledger.</summary>
public class TouchComponentsTests
{
    private const float TouchDistance = 1f;

    private static readonly FormKey OtherSpace = new(TestTargets.TargetMod, 0x101);

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x711), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x712), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x713), @"test\plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-30.5f, -2, 0), new Vector3(30.5f, 2, 4))));

    private static readonly ShapeCatalog Shapes =
        TestShapes.Create(TestTargets.TargetMod, "TouchComponentsData", TableModel, ItemModel, PlankModel);

    [Fact]
    public void ObjectItsRoundFoundTouchingStaysInItsCausesComponentWhenTheirPairTestsApart()
    {
        const int reached = 0;
        const int seed = 1;
        List<TargetObject> targets =
        [
            Place(reached, TableModel, TestTargets.Space, new Vector3(500, 0, 0)),
            Place(seed, TableModel, TestTargets.Space, Vector3.Zero),
        ];
        var protection = Protection.Build(targets, [], TestTargets.References(targets.Count));

        var (_, _, clusters) = TestTouchCascade.Execute(
            targets,
            Shapes,
            protection,
            [seed],
            TouchDistance,
            threads: 4,
            collectDiagnostics: true,
            (_, _) => new ScriptedRule([new Proposal(new TargetId(reached), new Cause.Touching(new TargetId(seed)))]));

        Assert.Equal(1, clusters.Stats.Components);
        Assert.Equal(2, clusters.Stats.LargestComponent);
        Assert.Equal(new[] { seed, reached }, Assert.Single(clusters.Diagnostics!.ComponentMembers));
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
        var protection = Protection.Build(targets, [TestTargets.Link(itemOnTable, partner)], TestTargets.References(targets.Count));

        var (ledger, _, clusters) =
            TestTouchCascade.Execute(targets, Shapes, protection, [seedTable], TouchDistance, threads: 4, collectDiagnostics: true);

        Assert.Equal(new Cause.Linked(new TargetId(itemOnTable)), ledger.Of(new TargetId(partner))!.Cause);
        Assert.Equal(1, clusters.Diagnostics!.Depth[itemOnTable]);
        Assert.Equal(2, clusters.Diagnostics.Depth[partner]);
        Assert.Equal(2, clusters.Stats.MaxDepth);
    }

    [Fact]
    public void TwoSeedsJoinedThroughARemovedObjectFormOneComponent()
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
        var protection = Protection.Build(targets, [], TestTargets.References(targets.Count));

        var (ledger, _, clusters) =
            TestTouchCascade.Execute(targets, Shapes, protection, [firstSeed, secondSeed], TouchDistance, threads: 4, collectDiagnostics: true);

        Assert.True(ledger.IsRemoved(new TargetId(plank)));
        Assert.Equal(1, clusters.Stats.Components);
        Assert.Equal(3, clusters.Stats.LargestComponent);
        Assert.Equal(new[] { firstSeed, secondSeed, plank }, Assert.Single(clusters.Diagnostics!.ComponentMembers));
    }

    private static TargetObject Place(int index, TestStatic model, FormKey space, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), model.Ref, space);

    /// <summary>Proposes the given removals in the first round and nothing after, whatever the geometry says.</summary>
    private sealed class ScriptedRule(IReadOnlyList<Proposal> firstRound) : IFollowUpRule
    {
        private bool _proposed;

        public RoundProposals Next(Ledger ledger, ImmutableArray<TargetId> removedLastRound)
        {
            var proposals = _proposed ? [] : firstRound;
            _proposed = true;
            return new RoundProposals(proposals, new TouchRound([.. proposals.Select(proposal => new TargetPair(((Cause.Touching)proposal.Cause).Touched.Index, proposal.Target.Index))], PairTestStats.Zero));
        }
    }
}
