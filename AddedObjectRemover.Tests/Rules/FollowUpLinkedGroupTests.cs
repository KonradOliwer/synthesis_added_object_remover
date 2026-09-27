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

    private static readonly BaseObjectShapeProvider Shapes = TestShapes.Create(TestTargets.TargetMod, "FollowUpLinkedGroupData", TableModel, ItemModel);

    /// <summary>A table (the seed) with an item on it, a shelf in another space linked to that item, an item on the shelf, and an unrelated table.</summary>
    private static readonly List<TargetObject> Targets =
    [
        Place(Table, TableModel, TestTargets.Space, Vector3.Zero),
        Place(ItemOnTable, ItemModel, TestTargets.Space, new Vector3(5, 5, 10)),
        Place(Shelf, TableModel, OtherSpace, new Vector3(3000, 0, 0)),
        Place(ItemOnShelf, ItemModel, OtherSpace, new Vector3(3000, 0, 10)),
        Place(Unrelated, TableModel, TestTargets.Space, new Vector3(-500, 0, 0)),
    ];

    private static readonly LinkedGroups Groups = LinkedGroups.Build(Targets, [new TargetLink(TestTargets.Key(ItemOnTable), TestTargets.Key(Shelf))]);

    private static readonly ObjectVisibility[] AllVisible = Enumerable.Repeat(ObjectVisibility.Visible, Targets.Count).ToArray();

    [Fact]
    public void EverythingTouchingFollowsLinkedPartnersIntoTheirOwnTouchSearch()
    {
        var clusters = TouchClusterFinder.Find(
            Targets, AllVisible, seeds: [Table], keptTooClose: [], Groups, Shapes, NewCache(), NewKeepRule(), TouchDistance, Options(), collectDiagnostics: true);

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
            Groups,
            new SupporterIndex(new Dictionary<FormKey, List<OtherObject>>(), Shapes, Options()),
            new TerrainHeights(new Dictionary<ExteriorCell, Mutagen.Bethesda.Skyrim.ILandscapeGetter>(), new Dictionary<FormKey, FormKey>()),
            Shapes,
            NewCache(),
            NewKeepRule(),
            TouchDistance,
            threshold: 0.5f,
            Options());

        Assert.Equal(
            new[] { (ItemOnTable, typeof(AnchoringRemoval)), (Shelf, typeof(LinkedRemoval)), (ItemOnShelf, typeof(AnchoringRemoval)) },
            anchoring.Removals.Select(removal => (removal.TargetIndex, removal.GetType())));
        Assert.Equal(new[] { (ItemOnTable, 1), (ItemOnShelf, 2) }, anchoring.Evaluations.Select(evaluation => (evaluation.TargetIndex, evaluation.Iteration)));
    }

    [Fact]
    public void ReachableSpacesFollowLinkedGroups()
    {
        Assert.Equal(new HashSet<FormKey> { TestTargets.Space, OtherSpace }, Groups.CollectReachableSpaces(Targets, [Table]));
        Assert.Equal(new HashSet<FormKey> { OtherSpace }, LinkedGroups.Build(Targets, []).CollectReachableSpaces(Targets, [Shelf]));
    }

    private static TargetObject Place(int index, TestStatic model, FormKey space, Vector3 position) =>
        new(TestTargets.Record(index), space, CellName: null, TestTargets.At(position), model.Ref, IsTeleportDoor: false, IsPrimitive: false, HasMapMarker: false);

    private static TriangleTreeCache NewCache() => new(Shapes.ReadGeometry);

    private static KeepReferencedRule NewKeepRule() => new(Targets, new Dictionary<FormKey, KeepReason>(), Groups);

    private static ParallelOptions Options() => new() { MaxDegreeOfParallelism = 4 };
}
