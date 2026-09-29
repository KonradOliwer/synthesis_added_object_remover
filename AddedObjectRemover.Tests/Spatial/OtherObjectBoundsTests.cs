using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Spatial;

/// <summary>Other mods' objects are found by where their bounds are, not where their origin is.</summary>
public class OtherObjectBoundsTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Bounds.esp");
    private static readonly Box CrateBox = new(new Vector3(-50), new Vector3(50));

    /// <summary>Farther from the origin than an exterior cell.</summary>
    private static readonly Vector3 FarOffset = new(10000, 0, 0);

    /// <summary>Wide enough to be kept in the index's large-object list.</summary>
    private static readonly Box CliffBox = new(new Vector3(-20000, -20000, 0), new Vector3(20000, 20000, 100));

    private static readonly TestStatic Crate = new(new FormKey(Mod, 0x801), @"test\crate.nif", TestMeshes.BoxTriangles(CrateBox));

    private static readonly TestStatic FarCrate = new(
        new FormKey(Mod, 0x802), @"test\farcrate.nif", TestMeshes.BoxTriangles(new Box(CrateBox.Min + FarOffset, CrateBox.Max + FarOffset)));

    private static readonly TestStatic Cliff = new(new FormKey(Mod, 0x803), @"test\cliff.nif", TestMeshes.BoxTriangles(CliffBox));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(Mod, "OtherObjectBoundsData", Crate, FarCrate, Cliff);

    [Fact]
    public void ObjectIsFoundWhereItsMeshIsNotWhereItsOriginIs()
    {
        var index = CreateIndex(Place(0, FarCrate, Vector3.Zero));

        Assert.Equal(new[] { 0 }, Collect(index, new Box(FarOffset, FarOffset)));
        Assert.Empty(Collect(index, new Box(Vector3.Zero, Vector3.Zero)));
    }

    [Fact]
    public void LargeObjectIsReturnedOnlyWhereItReaches()
    {
        var index = CreateIndex(Place(0, Cliff, Vector3.Zero), Place(1, Crate, new Vector3(50000, 0, 0)));

        Assert.Equal(1, index.Bounds.LargeObjectCount);
        Assert.Equal(new[] { 0 }, Collect(index, new Box(new Vector3(19000, 0, 50), new Vector3(19000, 0, 50))));
        Assert.Equal(new[] { 1 }, Collect(index, new Box(new Vector3(50000, 0, 0), new Vector3(50000, 0, 0))));
        Assert.Empty(Collect(index, new Box(new Vector3(0, 0, 500), new Vector3(0, 0, 500))));
    }

    [Fact]
    public void BoundingBoxZoneFindsAnObjectWhoseMeshLiesFarFromItsOrigin()
    {
        OtherObject[] rivals = [Place(0, FarCrate, Vector3.Zero)];

        Assert.Equal(new OtherId(0), FindInBoxZone(rivals, FarOffset));
        Assert.Null(FindInBoxZone(rivals, Vector3.Zero));
    }

    [Fact]
    public void BoundingBoxZoneReportsTheLowestId()
    {
        OtherObject[] rivals = [Place(0, Crate, new Vector3(30, 0, 0)), Place(1, Crate, new Vector3(-30, 0, 0))];

        Assert.Equal(new OtherId(0), FindInBoxZone(rivals, Vector3.Zero));
    }

    private static OtherId? FindInBoxZone(OtherObject[] rivals, Vector3 targetPosition)
    {
        var target = TestTargets.Create(0, TestTargets.At(targetPosition)) with { Base = Crate.Ref };
        var active = TestScenes.Create([target], rivals, Shapes).ActiveRivals(Replacements.None(rivals.Length), NpcHandling.CountLikeObjects);
        return TooCloseSearch.FindFirstCentreInBoxZone(target, active, Shapes, multiplier: 0f, new SpatialQueryScratch(), []);
    }

    private static OtherObject Place(int id, TestStatic model, Vector3 position) => TestShapes.Placed(Mod, id, model.Ref, position);

    private static OtherObjectIndex CreateIndex(params OtherObject[] objects) => OtherObjectIndex.Create(objects, Shapes, new ParallelOptions(), UntimedPhases.Instance, TimedPhase.RivalBoundsBuild);

    private static List<int> Collect(OtherObjectIndex index, Box area)
    {
        var candidates = new List<int>();
        index.Bounds.CollectCandidates(area, [], candidates);
        return candidates;
    }
}
