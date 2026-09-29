using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Follow-up removal repeats until nothing changes, linked groups included: a linked member removed
/// with a follow-up removal starts its own follow-up, even in another space.
/// </summary>
public class FollowUpLinkedGroupTests
{
    private const float TouchDistance = 1f;
    private const int Table = 0;
    private const int ItemOnTable = 1;
    private const int Shelf = 2;
    private const int ItemOnShelf = 3;
    private const int Unrelated = 4;

    private static readonly FormKey OtherSpace = new(TestTargets.TargetMod, 0x101);

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x702), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic FloorModel = new(
        new FormKey(TestTargets.TargetMod, 0x703), @"test\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-200, -200, -10), new Vector3(200, 200, 0))));

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly BaseObjectShapeProvider Shapes =
        TestShapes.Create(TestTargets.TargetMod, "FollowUpLinkedGroupData", TableModel, ItemModel, FloorModel);

    /// <summary>A table (the seed) with an item on it, a shelf in another space linked to that item, an item on the shelf, and an unrelated table.</summary>
    private static readonly List<TargetObject> Targets =
    [
        Place(Table, TableModel, TestTargets.Space, Vector3.Zero),
        Place(ItemOnTable, ItemModel, TestTargets.Space, new Vector3(5, 5, 10)),
        Place(Shelf, TableModel, OtherSpace, new Vector3(3000, 0, 0)),
        Place(ItemOnShelf, ItemModel, OtherSpace, new Vector3(3000, 0, 10)),
        Place(Unrelated, TableModel, TestTargets.Space, new Vector3(-500, 0, 0)),
    ];

    private static readonly Protection SceneProtection =
        Protection.Build(Targets, [TestTargets.Link(ItemOnTable, Shelf)], TestTargets.References(Targets.Count));

    private static readonly ObjectVisibility[] AllVisible = Enumerable.Repeat(ObjectVisibility.Visible, Targets.Count).ToArray();

    [Fact]
    public void EverythingTouchingFollowsLinkedPartnersIntoTheirOwnTouchSearch()
    {
        var (ledger, followUpRounds, clusters) =
            TestTouchCascade.Execute(Targets, Shapes, SceneProtection, [Table], TouchDistance, threads: 4, collectDiagnostics: true);

        Assert.Equal(
            new (int, Cause)[]
            {
                (ItemOnTable, new Cause.Touching(new TargetId(Table))),
                (Shelf, new Cause.Linked(new TargetId(ItemOnTable))),
                (ItemOnShelf, new Cause.Touching(new TargetId(Shelf))),
            },
            followUpRounds.SelectMany(round => ledger.RemovedIn(round)).Select(target => (target.Index, ledger.Of(target)!.Cause)));
        Assert.Equal(
            new Removal[]
            {
                new TouchingRemoval(ItemOnTable, TouchedTargetIndex: Table),
                new LinkedRemoval(Shelf, LinkedToTargetIndex: ItemOnTable),
                new TouchingRemoval(ItemOnShelf, TouchedTargetIndex: Shelf),
            },
            clusters.Removals);
        Assert.Equal(1, clusters.Stats.Components);
        Assert.Equal(1, clusters.Stats.ComponentsWithRemovals);
        Assert.Equal(4, clusters.Stats.LargestComponent);
        Assert.Equal(2, clusters.Stats.Levels);
        Assert.Equal(3, clusters.Stats.MaxDepth);
    }

    [Fact]
    public void AnchoringFollowsLinkedPartnersAndReevaluatesOnlyTheirNeighbours()
    {
        var anchoring = AnchoringRemover.Run(
            Targets,
            AllVisible,
            seeds: [Table],
            keptTooClose: [],
            new SupporterIndex(new Dictionary<FormKey, List<OtherObject>>(), Shapes, Options()),
            NoTerrain(),
            Shapes,
            NewCache(),
            SceneProtection,
            TouchDistance,
            threshold: 0.5f,
            Options());

        Assert.Equal(
            new[] { (ItemOnTable, typeof(AnchoringRemoval)), (Shelf, typeof(LinkedRemoval)), (ItemOnShelf, typeof(AnchoringRemoval)) },
            anchoring.Removals.Select(removal => (removal.TargetIndex, removal.GetType())));
        Assert.Equal(new[] { (ItemOnTable, 1), (ItemOnShelf, 2) }, anchoring.Evaluations.Select(evaluation => (evaluation.TargetIndex, evaluation.Iteration)));
    }

    [Fact]
    public void CandidateKeptByItsSupportButRemovedWithItsLinkedPartnerIsReportedAsRemovedAsLinked()
    {
        const int seedTable = 0;
        const int itemOnTable = 1;
        const int crateBesideTable = 2;
        List<TargetObject> targets =
        [
            Place(seedTable, TableModel, TestTargets.Space, Vector3.Zero),
            Place(itemOnTable, ItemModel, TestTargets.Space, new Vector3(5, 5, 10)),
            Place(crateBesideTable, ItemModel, TestTargets.Space, new Vector3(23, 0, 0)),
        ];
        var protection = Protection.Build(targets, [TestTargets.Link(itemOnTable, crateBesideTable)], TestTargets.References(targets.Count));
        var floor = TestShapes.Placed(OtherMod, 0, FloorModel.Ref, Vector3.Zero);

        var anchoring = AnchoringRemover.Run(
            targets,
            Enumerable.Repeat(ObjectVisibility.Visible, targets.Count).ToArray(),
            seeds: [seedTable],
            keptTooClose: [],
            new SupporterIndex(new Dictionary<FormKey, List<OtherObject>> { [TestTargets.Space] = [floor] }, Shapes, Options()),
            NoTerrain(),
            Shapes,
            NewCache(),
            protection,
            TouchDistance,
            threshold: 0.9f,
            Options());

        Assert.Equal(
            new[] { (itemOnTable, typeof(AnchoringRemoval)), (crateBesideTable, typeof(LinkedRemoval)) },
            anchoring.Removals.Select(removal => (removal.TargetIndex, removal.GetType())));
        var crate = Assert.Single(anchoring.Evaluations, evaluation => evaluation.TargetIndex == crateBesideTable);
        Assert.False(crate.Removed);
        Assert.True(crate.RemovedAsLinked);
        Assert.False(Assert.Single(anchoring.Evaluations, evaluation => evaluation.TargetIndex == itemOnTable).RemovedAsLinked);
    }

    [Fact]
    public void ReachableSpacesFollowLinkedGroups()
    {
        Assert.Equal(new HashSet<FormKey> { TestTargets.Space, OtherSpace }, SceneProtection.Groups.CollectReachableSpaces(Targets, [Table]));
        Assert.Equal(new HashSet<FormKey> { OtherSpace }, LinkedGroups.Build(Targets.Count, []).CollectReachableSpaces(Targets, [Shelf]));
    }

    private static TargetObject Place(int index, TestStatic model, FormKey space, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), model.Ref, space);

    private static TriangleTreeCache NewCache() => new(Shapes.ReadGeometry);

    private static TerrainHeights NoTerrain() =>
        new(new Dictionary<ExteriorCell, Mutagen.Bethesda.Skyrim.ILandscapeGetter>(), new Dictionary<FormKey, FormKey>());

    private static ParallelOptions Options() => new() { MaxDegreeOfParallelism = 4 };
}
