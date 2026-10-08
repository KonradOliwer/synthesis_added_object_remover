using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>Protected objects are judged like any other by the support cascade: held only when they would lose their support.</summary>
public class SupportProtectionTests
{
    private const float TouchDistance = 1f;
    private const int SeedTable = 0;
    private const int Protected = 1;

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly TestStatic TableModel = new(
        new FormKey(TestTargets.TargetMod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic ItemModel = new(
        new FormKey(TestTargets.TargetMod, 0x702), @"test\item.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));

    /// <summary>Only its lowest tip comes within the touch distance of the table, and no surface sample lands that close.</summary>
    private static readonly TestStatic NeedleModel = new(
        new FormKey(TestTargets.TargetMod, 0x704),
        @"test\needle.nif",
        [new MeshTriangle(new Vector3(-20, 0, 100), new Vector3(20, 0, 100), Vector3.Zero)]);

    private static readonly TestStatic FloorModel = new(
        new FormKey(TestTargets.TargetMod, 0x703), @"test\floor.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-200, -200, -10), new Vector3(200, 200, 0))));

    private static readonly IBaseObjectShapes Shapes =
        TestShapes.Create(TestTargets.TargetMod, "SupportProtectionData", TableModel, ItemModel, NeedleModel, FloorModel);

    [Fact]
    public void ProtectedObjectRestingOnlyOnRemovedSeedIsHeldWithLostSupportCause()
    {
        var run = Execute(itemPosition: new Vector3(5, 5, 10), otherSupporters: []);

        var held = Assert.IsType<Decision.Kept>(run.RemovalDecisions.Of(new TargetId(Protected)));
        var lost = Assert.IsType<RemovalReason.LostSupport>(held.Reason);
        Assert.Equal(new TargetId(SeedTable), lost.MainSupporter);
        var kept = Assert.Single(run.Kept);
        Assert.Equal(Protected, kept.TargetIndex);
        Assert.Equal(SeedTable, kept.TouchedTargetIndex);
        Assert.True(Assert.Single(run.Evaluations, evaluation => evaluation.TargetIndex == Protected).Held);
    }

    [Fact]
    public void ProtectedObjectMostlySupportedByOthersIsNeitherHeldNorRemoved()
    {
        var floor = TestShapes.Placed(OtherMod, 0, FloorModel.Base, Vector3.Zero);

        var run = Execute(itemPosition: new Vector3(23, 0, 0), otherSupporters: [floor]);

        Assert.Null(run.RemovalDecisions.Of(new TargetId(Protected)));
        Assert.Empty(run.Kept);
        var evaluation = Assert.Single(run.Evaluations, evaluation => evaluation.TargetIndex == Protected);
        Assert.False(evaluation.Held);
        Assert.False(evaluation.Removed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeptWithoutContactsCountsOnlyUnprotectedCandidates(bool isProtected)
    {
        var run = Execute(itemPosition: new Vector3(5, 5, 10.5f), otherSupporters: [], isProtected, NeedleModel);

        var evaluation = Assert.Single(run.Evaluations, evaluation => evaluation.TargetIndex == Protected);
        Assert.Equal(0, evaluation.Contacts.ContactPoints);
        Assert.Equal(1, run.Result.Work.Candidates);
        Assert.Equal(isProtected ? 0 : 1, run.Result.Work.KeptWithoutContacts);
    }

    private static TestSupportCascade.Run Execute(Vector3 itemPosition, List<OtherObject> otherSupporters, bool isProtected = true, TestStatic? itemModel = null)
    {
        List<TargetObject> targets =
        [
            TestTargets.Create(SeedTable, TestTargets.At(Vector3.Zero), TableModel.Base, TestTargets.Space),
            TestTargets.Create(Protected, TestTargets.At(itemPosition), (itemModel ?? ItemModel).Base, TestTargets.Space),
        ];
        var references = TestTargets.References(
            targets.Count,
            isProtected
                ? new Dictionary<int, KeepReason> { [Protected] = TestKeepReasons.Quest }
                : []);
        var protection = ObjectsToKeep.Build(targets, [], references);
        return TestSupportCascade.Execute(
            targets,
            Shapes,
            protection,
            [SeedTable],
            TestScenes.CreateWithSupportOnlyObjects(targets, otherSupporters, Shapes).VisibleObjectsOfAnyPlugin(),
            TestGround.NoTerrain(),
            TouchDistance,
            threshold: 0.9f,
            threads: 1);
    }
}
