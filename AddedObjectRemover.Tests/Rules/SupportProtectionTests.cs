using System.Numerics;
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

    private static readonly ShapeCatalog Shapes =
        TestShapes.Create(TestTargets.TargetMod, "SupportProtectionData", TableModel, ItemModel, NeedleModel, FloorModel);

    [Fact]
    public void ProtectedObjectRestingOnlyOnRemovedSeedIsHeldWithLostSupportCause()
    {
        var run = Execute(itemPosition: new Vector3(5, 5, 10), otherSupporters: []);

        var held = Assert.IsType<Verdict.Held>(run.Ledger.Of(new TargetId(Protected)));
        var lost = Assert.IsType<Cause.LostSupport>(held.Cause);
        Assert.Equal(new TargetId(SeedTable), lost.MainSupporter);
        var kept = Assert.Single(run.Anchoring.Kept);
        Assert.Equal(Protected, kept.TargetIndex);
        Assert.Equal(SeedTable, kept.TouchedTargetIndex);
        Assert.True(Assert.Single(run.Anchoring.Evaluations, evaluation => evaluation.TargetIndex == Protected).Held);
    }

    [Fact]
    public void ProtectedObjectMostlySupportedByOthersIsNeitherHeldNorRemoved()
    {
        var floor = TestShapes.Placed(OtherMod, 0, FloorModel.Ref, Vector3.Zero);

        var run = Execute(itemPosition: new Vector3(23, 0, 0), otherSupporters: [floor]);

        Assert.Null(run.Ledger.Of(new TargetId(Protected)));
        Assert.Empty(run.Anchoring.Kept);
        var evaluation = Assert.Single(run.Anchoring.Evaluations, evaluation => evaluation.TargetIndex == Protected);
        Assert.False(evaluation.Held);
        Assert.False(evaluation.Removed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeptWithoutContactsCountsOnlyUnprotectedCandidates(bool isProtected)
    {
        var run = Execute(itemPosition: new Vector3(5, 5, 10.5f), otherSupporters: [], isProtected, NeedleModel);

        var evaluation = Assert.Single(run.Anchoring.Evaluations, evaluation => evaluation.TargetIndex == Protected);
        Assert.Equal(0, evaluation.Contacts.ContactPoints);
        Assert.Equal(1, run.Anchoring.Stats.Candidates);
        Assert.Equal(isProtected ? 0 : 1, run.Anchoring.Stats.KeptWithoutContacts);
    }

    private static TestSupportCascade.Run Execute(Vector3 itemPosition, List<OtherObject> otherSupporters, bool isProtected = true, TestStatic? itemModel = null)
    {
        List<TargetObject> targets =
        [
            TestTargets.Create(SeedTable, TestTargets.At(Vector3.Zero), TableModel.Ref, TestTargets.Space),
            TestTargets.Create(Protected, TestTargets.At(itemPosition), (itemModel ?? ItemModel).Ref, TestTargets.Space),
        ];
        var references = TestTargets.References(
            targets.Count,
            isProtected
                ? new Dictionary<int, KeepReason> { [Protected] = new(KeepKind.NonPlacedReference, "QUST record", "linked from QUST") }
                : []);
        var protection = Protection.Build(targets, [], references);
        return TestSupportCascade.Execute(
            targets,
            Shapes,
            protection,
            [SeedTable],
            TestScenes.CreateWithBackdrop(targets, otherSupporters, Shapes).Solids(),
            new TerrainHeights(new Dictionary<ExteriorCell, Mutagen.Bethesda.Skyrim.ILandscapeGetter>(), new Dictionary<FormKey, FormKey>()),
            TouchDistance,
            threshold: 0.9f,
            threads: 1);
    }
}
