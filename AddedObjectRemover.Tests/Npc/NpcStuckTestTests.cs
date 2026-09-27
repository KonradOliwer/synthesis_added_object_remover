using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Npc;

public class NpcStuckTestTests
{
    private static readonly PlacedTransform Identity = TestTargets.At(Vector3.Zero);
    private static readonly Box NpcBodyBox = new(new Vector3(-20, -20, 0), new Vector3(20, 20, 80));
    private static readonly Box Platform = new(new Vector3(-50, -50, -20), new Vector3(50, 50, 0));

    private static bool IsStuck(MeshTriangleTree objectTree, Box npcBodyBox, PlacedTransform npcTransform) =>
        NpcStuckTest.IsAnyBodyStuck(objectTree, Identity, SingleBody(npcBodyBox), npcTransform, new NpcScratch());

    private static NpcBodySet SingleBody(Box box) => new([new NpcBody(NpcSizeSource.BodyMesh, box, null)], box);

    [Fact]
    public void StandingOnTopIsContactNotStuck() =>
        Assert.False(IsStuck(BoxMesh.CreateTree(Platform), NpcBodyBox, TestTargets.At(new Vector3(0, 0, 0))));

    [Fact]
    public void SinkingPastTheClearanceIsStuck() =>
        Assert.True(IsStuck(BoxMesh.CreateTree(Platform), NpcBodyBox, TestTargets.At(new Vector3(0, 0, -10))));

    [Fact]
    public void SinkingLessThanTheClearanceIsNotStuck() =>
        Assert.False(IsStuck(BoxMesh.CreateTree(Platform), NpcBodyBox, TestTargets.At(new Vector3(0, 0, -(NpcStuckTest.FootClearance - 2)))));

    [Fact]
    public void LeaningAgainstTheSideIsContactNotStuck() =>
        Assert.False(IsStuck(BoxMesh.CreateTree(Platform), NpcBodyBox, TestTargets.At(new Vector3(70 - (NpcStuckTest.FootClearance - 2), 0, -15))));

    [Fact]
    public void PointBodyStandingOnTopIsNotStuck() =>
        Assert.False(IsStuck(BoxMesh.CreateTree(Platform), Box.Zero, TestTargets.At(Vector3.Zero)));

    [Fact]
    public void PointBodySunkPastTheClearanceIsStuck() =>
        Assert.True(IsStuck(BoxMesh.CreateTree(Platform), Box.Zero, TestTargets.At(new Vector3(0, 0, -(NpcStuckTest.FootClearance + 2)))));

    [Fact]
    public void LowBodySunkLessThanTheClearanceIsNotStuck()
    {
        var lowBody = new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 6));

        Assert.False(IsStuck(BoxMesh.CreateTree(Platform), lowBody, TestTargets.At(new Vector3(0, 0, -(NpcStuckTest.FootClearance - 2)))));
    }

    [Fact]
    public void BodyIsShrunkInWorldUnitsWhateverTheReferenceScale()
    {
        var core = NpcStuckTest.GetCore(NpcBodyBox, bodyScale: 2f);

        Assert.Equal(-20 + NpcStuckTest.FootClearance / 2, core.Min.X);
        Assert.Equal(NpcStuckTest.FootClearance / 2, core.Min.Z);
        Assert.Equal(80 - NpcStuckTest.FootClearance / 2, core.Max.Z);
    }

    [Fact]
    public void InsideAClosedShellIsStuck()
    {
        var shell = BoxMesh.CreateTree(new Box(new Vector3(-100), new Vector3(100)));

        Assert.True(IsStuck(shell, NpcBodyBox, TestTargets.At(new Vector3(0, 0, -40))));
    }

    [Fact]
    public void InsideAnOpenShellIsNotStuck()
    {
        var openShell = TestMeshes.Tree(TestMeshes.BoxWithoutFace(new Box(new Vector3(-100), new Vector3(100)), v => v.X == 100));

        Assert.False(IsStuck(openShell, NpcBodyBox, TestTargets.At(new Vector3(0, 0, -40))));
    }

    [Fact]
    public void CombinedMissRejectsWithoutTestingEachBody()
    {
        var farAway = new Box(new Vector3(-5, -5, 500), new Vector3(5, 5, 580));
        var evenFartherAway = new Box(new Vector3(-5, -5, 1000), new Vector3(5, 5, 1080));
        var bodies = NpcBodySet.Of([
            new NpcBody(NpcSizeSource.BodyMesh, farAway, null),
            new NpcBody(NpcSizeSource.BodyMesh, evenFartherAway, null)]);
        var scratch = new NpcScratch();

        var stuck = NpcStuckTest.IsAnyBodyStuck(BoxMesh.CreateTree(Platform), Identity, bodies, TestTargets.At(new Vector3(0, 0, 0)), scratch);

        Assert.False(stuck);
        Assert.Equal(1, scratch.CoreTests);
    }

    [Fact]
    public void OnlyTheStuckBodyOfSeveralPossibleBodiesIsFound()
    {
        var besideThePlatform = new Box(new Vector3(60, -5, 0), new Vector3(70, 5, 80));
        var sinksIntoThePlatform = NpcBodyBox;
        var bodies = NpcBodySet.Of([
            new NpcBody(NpcSizeSource.BodyMesh, besideThePlatform, null),
            new NpcBody(NpcSizeSource.BodyMesh, sinksIntoThePlatform, null)]);
        var scratch = new NpcScratch();

        var stuck = NpcStuckTest.IsAnyBodyStuck(BoxMesh.CreateTree(Platform), Identity, bodies, TestTargets.At(new Vector3(0, 0, -10)), scratch);

        Assert.True(stuck);
        Assert.Equal(3, scratch.CoreTests);
    }
}
