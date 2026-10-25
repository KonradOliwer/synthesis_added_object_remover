using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The shortcuts of the support questions give the answers of the full measurements: whether an object rests (against its whole
/// drop) and its contacts with target objects taken out (against a new contact search in the world without them). The object
/// judged is always another mod's object with id 0.
/// </summary>
public class SupportShortcutTests
{
    private const float TouchGap = 1f;
    private const float BlockHeight = 50f;
    private const float LanternHeight = 6f;
    private const float TileHeight = 0.5f;

    /// <summary>A share of the touch gap just inside it, and one just outside it.</summary>
    private const float JustInside = 0.9f;
    private const float JustOutside = 1.1f;

    /// <summary>How far the lantern's bottom lies inside the block when sunk into it: less than half the lantern's height.</summary>
    private const float SunkDepth = 2f;

    /// <summary>Far above the block, by more than the touch gap and the rounding margin.</summary>
    private const float HighAbove = 100f;

    /// <summary>How far the lowest point a ray reaches lies from the block's top in the two scenes at that limit.</summary>
    private const float NearLowestReach = 0.5f;

    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    private static readonly TestStatic LanternModel = new(
        new FormKey(TestTargets.TargetMod, 0x731), @"test\shortcuts\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, LanternHeight))));

    private static readonly TestStatic BlockModel = new(
        new FormKey(TestTargets.TargetMod, 0x732), @"test\shortcuts\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, BlockHeight))));

    /// <summary>Wider than the lantern, so its underside covers the lantern's top.</summary>
    private static readonly TestStatic BoardModel = new(
        new FormKey(TestTargets.TargetMod, 0x733), @"test\shortcuts\board.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 4))));

    private static readonly TestStatic TileModel = new(
        new FormKey(TestTargets.TargetMod, 0x734), @"test\shortcuts\tile.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-8, -8, 0), new Vector3(8, 8, TileHeight))));

    /// <summary>Long enough to lie across three tiles side by side.</summary>
    private static readonly TestStatic PlankModel = new(
        new FormKey(TestTargets.TargetMod, 0x735), @"test\shortcuts\plank.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-30, -10, 0), new Vector3(30, 10, 2))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(TestTargets.TargetMod, "SupportShortcutsData", LanternModel, BlockModel, BoardModel, TileModel, PlankModel);

    /// <summary>On the ground of <see cref="TestGround.FlatAtZero"/>.</summary>
    private static readonly Vector3 InTheTerrainCell = new(100, 100, 0);

    public enum DropScene
    {
        AboveTerrainWithinGap,
        AboveTerrainOutsideGap,
        AboveBlockWithinGap,
        AboveBlockOutsideGap,
        SunkIntoBlock,
        OnATargetBlock,
        UnderABoardSunkIntoItsTop,
        BlockTopJustAboveLowestReach,
        BlockTopJustBelowLowestReach,
        NothingBelow,
    }

    [Theory]
    [InlineData(DropScene.AboveTerrainWithinGap, true)]
    [InlineData(DropScene.AboveTerrainOutsideGap, false)]
    [InlineData(DropScene.AboveBlockWithinGap, true)]
    [InlineData(DropScene.AboveBlockOutsideGap, false)]
    [InlineData(DropScene.SunkIntoBlock, true)]
    [InlineData(DropScene.OnATargetBlock, true)]
    [InlineData(DropScene.UnderABoardSunkIntoItsTop, false)]
    [InlineData(DropScene.BlockTopJustAboveLowestReach, false)]
    [InlineData(DropScene.BlockTopJustBelowLowestReach, false)]
    [InlineData(DropScene.NothingBelow, false)]
    public void RestsAnswersAsTheWholeDrop(DropScene name, bool rests)
    {
        var scene = CreateDropScene(name);
        var drops = new DropFinder(CreateSurfaceContacts(scene), scene.Triangles, scene.Terrain, TouchGap);
        var standing = StandingOf(scene.Others[0]);
        var queries = new OtherObjectQueries();

        var measured = drops.FindDrop(standing, scene.World, queries) is { } drop && drops.RestsAt(drop);

        Assert.Equal(rests, measured);
        Assert.Equal(measured, drops.Rests(standing, scene.World, queries));
    }

    /// <summary>
    /// A plank of another mod lies across two tiles of the cleaned mod and a tile of another mod, all on the terrain, so its
    /// contacts list two target objects, another mod's object and the terrain.
    /// </summary>
    [Fact]
    public void ContactsWithoutTargetsEqualANewSearchWithoutThem()
    {
        var scene = new Scene(
            [Tile(0, InTheTerrainCell + new Vector3(-20, 0, 0)), Tile(1, InTheTerrainCell)],
            [TestShapes.Placed(OtherMod, 0, PlankModel.Base, InTheTerrainCell + new Vector3(0, 0, TileHeight)), TestShapes.Placed(OtherMod, 1, TileModel.Base, InTheTerrainCell + new Vector3(20, 0, 0))],
            TestGround.FlatAtZero());
        var support = new OtherObjectSupport(CreateSurfaceContacts(scene), TouchGap);
        var standing = StandingOf(scene.Others[0]);
        var queries = new OtherObjectQueries();
        var hits = support.FindHits(standing, scene.World, queries);

        Assert.Equal(
            [Supporter.Target(0), Supporter.Target(1), Supporter.Placed(new OtherId(1)), Supporter.Terrain],
            hits.Contacts().Supporters.Select(entry => entry.Supporter));
        List<TargetId>[] takenOutSets = [[new TargetId(0)], [new TargetId(1)], [new TargetId(0), new TargetId(1)]];
        foreach (var takenOut in takenOutSets)
        {
            var searched = support.FindContacts(standing, scene.World.Without(takenOut), queries);
            var shortcut = hits.ContactsWithout(takenOut);

            Assert.Equal(searched.ContactPoints, shortcut.ContactPoints);
            Assert.Equal(BitConverter.SingleToInt32Bits(searched.TotalWeight), BitConverter.SingleToInt32Bits(shortcut.TotalWeight));
            Assert.Equal(
                searched.Supporters.Select(entry => (entry.Supporter, BitConverter.SingleToInt32Bits(entry.Weight))),
                shortcut.Supporters.Select(entry => (entry.Supporter, BitConverter.SingleToInt32Bits(entry.Weight))));
        }
    }

    /// <summary>The objects of one scene, the triangles of their meshes and the world they make.</summary>
    private sealed record Scene(List<TargetObject> Targets, List<OtherObject> Others, ITerrainHeights Terrain)
    {
        public TriangleStore Triangles { get; } = new(Shapes.ReadTriangles);

        public SupportWorld World { get; } = CreateWorld(Targets, Others);

        private static SupportWorld CreateWorld(List<TargetObject> targets, List<OtherObject> others)
        {
            var caches = TestScenes.CreateWithSupportOnlyObjects(targets, others, Shapes);
            return new SupportWorld(targets, caches.VisibleTargets, _ => true, caches.VisibleObjectsOfAnyPlugin(), SupportWorld.NoChanges, Shapes);
        }
    }

    /// <summary>The lantern (id 0) and what is below it.</summary>
    private static Scene CreateDropScene(DropScene name) => name switch
    {
        DropScene.AboveTerrainWithinGap => new Scene([], [Lantern(InTheTerrainCell + Up(JustInside * TouchGap))], TestGround.FlatAtZero()),
        DropScene.AboveTerrainOutsideGap => new Scene([], [Lantern(InTheTerrainCell + Up(JustOutside * TouchGap))], TestGround.FlatAtZero()),
        DropScene.AboveBlockWithinGap => OnABlock(BlockHeight + JustInside * TouchGap),
        DropScene.AboveBlockOutsideGap => OnABlock(BlockHeight + JustOutside * TouchGap),
        DropScene.SunkIntoBlock => OnABlock(BlockHeight - SunkDepth),
        DropScene.OnATargetBlock => new Scene(
            [TestTargets.Create(0, TestTargets.At(Vector3.Zero), BlockModel.Base, TestTargets.Space)], [Lantern(Up(BlockHeight))], TestGround.NoTerrain()),
        DropScene.UnderABoardSunkIntoItsTop => UnderABoard(),
        DropScene.BlockTopJustAboveLowestReach => OnABlock(BlockHeight + TouchGap + DropFinder.RoundingMargin - NearLowestReach),
        DropScene.BlockTopJustBelowLowestReach => OnABlock(BlockHeight + TouchGap + DropFinder.RoundingMargin + NearLowestReach),
        DropScene.NothingBelow => new Scene([], [Lantern(Up(HighAbove))], TestGround.NoTerrain()),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>The lantern's bottom at <paramref name="height"/> above another mod's block (id 1) standing at the origin.</summary>
    private static Scene OnABlock(float height) => new([], [Lantern(Up(height)), Block(1)], TestGround.NoTerrain());

    /// <summary>
    /// The lantern high above the block, with another mod's board (id 2) lying on it whose underside lies inside the lantern's
    /// top, within the touch gap: the lantern's top is sunk into the board, which does not hold it up.
    /// </summary>
    private static Scene UnderABoard()
    {
        var boardBottom = HighAbove + LanternHeight - TouchGap / 2;
        return new Scene([], [Lantern(Up(HighAbove)), Block(1), TestShapes.Placed(OtherMod, 2, BoardModel.Base, Up(boardBottom))], TestGround.NoTerrain());
    }

    private static SurfaceContacts CreateSurfaceContacts(Scene scene) => new(scene.Terrain, scene.Triangles, TestSurfaceSamples.Over(scene.Triangles), TouchGap);

    private static StandingObject StandingOf(OtherObject other)
    {
        var shape = Shapes.Of(other.Base);
        return new StandingObject(other.SpaceKey, other.Transform, shape.MeshPath!, OrientedBox.FromLocal(shape.Box, other.Transform), Supporter.Placed(other.Id));
    }

    private static Vector3 Up(float height) => new(0, 0, height);

    private static OtherObject Lantern(Vector3 position) => TestShapes.Placed(OtherMod, 0, LanternModel.Base, position);

    private static OtherObject Block(int id) => TestShapes.Placed(OtherMod, id, BlockModel.Base, Vector3.Zero);

    private static TargetObject Tile(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), TileModel.Base, TestTargets.Space);
}
