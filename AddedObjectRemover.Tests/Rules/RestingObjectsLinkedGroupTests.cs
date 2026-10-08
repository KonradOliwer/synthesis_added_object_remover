using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Also-remove rounds repeat until nothing changes, linked groups included: a linked member removed
/// with an also-remove removal starts its own also-remove rounds, even in another space.
/// </summary>
public class RestingObjectsLinkedGroupTests
{
    private const float TouchDistance = 1f;
    private const int Table = 0;
    private const int ItemOnTable = 1;
    private const int Shelf = 2;
    private const int ItemOnShelf = 3;
    private const int Unrelated = 4;

    private static readonly RecordKey OtherSpace = TestTargets.SpaceKey(0x101);

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x702), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    private static readonly TestStatic FloorModel = new(
        new FormKey(TestTargets.TargetMod, 0x703), @"test\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-200, -200, -10), new Vector3(200, 200, 0))));

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "RestingObjectsLinkedGroupData", TableModel, ItemModel, FloorModel);

    /// <summary>A table (the seed) with an item on it, a shelf in another space linked to that item, an item on the shelf, and an unrelated table.</summary>
    private static readonly List<TargetObject> Targets =
    [
        Place(Table, TableModel, TestTargets.Space, Vector3.Zero),
        Place(ItemOnTable, ItemModel, TestTargets.Space, new Vector3(5, 5, 10)),
        Place(Shelf, TableModel, OtherSpace, new Vector3(3000, 0, 0)),
        Place(ItemOnShelf, ItemModel, OtherSpace, new Vector3(3000, 0, 10)),
        Place(Unrelated, TableModel, TestTargets.Space, new Vector3(-500, 0, 0)),
    ];

    private static readonly ObjectsToKeep SceneProtection =
        ObjectsToKeep.Build(Targets, [TestTargets.Link(ItemOnTable, Shelf)], TestTargets.References(Targets.Count));

    [Fact]
    public void EverythingTouchingFollowsLinkedPartnersIntoTheirOwnTouchSearch()
    {
        var run = TestTouchCascade.Execute(Targets, Shapes, SceneProtection, [Table], TouchDistance, threads: 4, collectDiagnostics: false);
        var (decisions, alsoRemoveRounds, stats) = (run.RemovalDecisions, run.AlsoRemoveRounds, run.TouchChains.Stats);

        Assert.Equal(
            new (int, RemovalReason)[]
            {
                (ItemOnTable, new RemovalReason.Touching(new TargetId(Table))),
                (Shelf, new RemovalReason.LinkedTo(new TargetId(ItemOnTable))),
                (ItemOnShelf, new RemovalReason.Touching(new TargetId(Shelf))),
            },
            alsoRemoveRounds.SelectMany(round => decisions.RemovedIn(round)).Select(target => (target.Index, decisions.Of(target)!.Reason)));
        Assert.Equal(1, stats.Components);
        Assert.Equal(1, stats.ComponentsWithRemovals);
        Assert.Equal(4, stats.LargestComponent);
        Assert.Equal(2, stats.Levels);
        Assert.Equal(3, stats.MaxDepth);
    }

    [Fact]
    public void AnchoringFollowsLinkedPartnersAndReevaluatesOnlyTheirNeighbours()
    {
        var run = TestSupportCascade.Execute(
            Targets,
            Shapes,
            SceneProtection,
            [Table],
            TestScenes.CreateWithSupportOnlyObjects(Targets, [], Shapes).VisibleObjectsOfAnyPlugin(),
            NoTerrain(),
            TouchDistance,
            threshold: 0.5f,
            threads: 4);

        Assert.Equal(
            new[] { (ItemOnTable, typeof(RemovalReason.LostSupport)), (Shelf, typeof(RemovalReason.LinkedTo)), (ItemOnShelf, typeof(RemovalReason.LostSupport)) },
            DescribeRemovals(run));
        Assert.Equal(
            new[] { (ItemOnTable, 1), (ItemOnShelf, 2) },
            AnchoringRows.Join(run.Result).Select(evaluation => (evaluation.TargetIndex, evaluation.Iteration)));
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
        var protection = ObjectsToKeep.Build(targets, [TestTargets.Link(itemOnTable, crateBesideTable)], TestTargets.References(targets.Count));
        var floor = TestShapes.Placed(OtherMod, 0, FloorModel.Base, Vector3.Zero);

        var run = TestSupportCascade.Execute(
            targets,
            Shapes,
            protection,
            [seedTable],
            TestScenes.CreateWithSupportOnlyObjects(targets, [floor], Shapes).VisibleObjectsOfAnyPlugin(),
            NoTerrain(),
            TouchDistance,
            threshold: 0.9f,
            threads: 4);

        Assert.Equal(new[] { (itemOnTable, typeof(RemovalReason.LostSupport)), (crateBesideTable, typeof(RemovalReason.LinkedTo)) }, DescribeRemovals(run));
        var evaluations = AnchoringRows.Join(run.Result);
        var crate = Assert.Single(evaluations, evaluation => evaluation.TargetIndex == crateBesideTable);
        Assert.False(crate.Removed);
        Assert.True(crate.RemovedAsLinked);
        Assert.False(Assert.Single(evaluations, evaluation => evaluation.TargetIndex == itemOnTable).RemovedAsLinked);
    }

    [Fact]
    public void ReachableSpacesFollowLinkedGroups()
    {
        Assert.Equal(new HashSet<RecordKey> { TestTargets.Space, OtherSpace }, ReachableSpaces.CollectReachableSpaces(SceneProtection.Groups, Targets, [Table]));
        Assert.Equal(new HashSet<RecordKey> { OtherSpace }, ReachableSpaces.CollectReachableSpaces(LinkedGroups.Build(Targets.Count, []), Targets, [Shelf]));
    }

    /// <returns>The follow-up rounds' removals in decision order, with the type of their reason.</returns>
    private static IEnumerable<(int, Type)> DescribeRemovals(TestSupportCascade.Run run) =>
        run.AlsoRemoveRounds.SelectMany(round => run.RemovalDecisions.RemovedIn(round)).Select(target => (target.Index, run.RemovalDecisions.Of(target)!.Reason.GetType()));

    private static TargetObject Place(int index, TestStatic model, RecordKey space, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), model.Base, space);

    private static TerrainHeights NoTerrain() =>
        TestGround.NoTerrain();
}
