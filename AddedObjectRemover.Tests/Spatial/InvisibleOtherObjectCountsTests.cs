using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Spatial;

public class InvisibleOtherObjectCountsTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Census.esp");
    private static readonly RecordKey SpaceWithOnlyInvisibleTargets = TestTargets.SpaceKey(0x200);
    private static readonly RecordKey SpaceWithoutTargets = TestTargets.SpaceKey(0x201);
    private static readonly BaseKey MissingBase = new(new FormKey(Mod, 0x7FF).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static readonly TestStatic Crate = new(
        new FormKey(Mod, 0x801), @"test\crate.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50), new Vector3(50))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Mod, "InvisibleOtherObjectCountsData", Crate);

    [Fact]
    public void CountsEveryInvisibleOtherModObjectOfTheSpacesWithAVisibleTarget()
    {
        List<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(Vector3.Zero), Crate.Base, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(Vector3.Zero), baseKey: null, SpaceWithOnlyInvisibleTargets),
        ];
        var farAway = new Vector3(50000, 0, 0);
        List<OtherObject> otherModObjects =
        [
            TestShapes.Placed(Mod, 0, Crate.Base, farAway),
            TestShapes.Placed(Mod, 1, MissingBase, farAway),
            TestShapes.Placed(Mod, 2, Crate.Base, farAway) with { HasMapMarker = true },
            TestShapes.Placed(Mod, 3, MissingBase, Vector3.Zero) with { SpaceKey = SpaceWithOnlyInvisibleTargets },
            TestNpcs.Place(Mod, 4, MissingBase.Record.ToFormKey(), Vector3.Zero, default) with { SpaceKey = SpaceWithOnlyInvisibleTargets },
            TestShapes.Placed(Mod, 5, MissingBase, Vector3.Zero) with { SpaceKey = SpaceWithoutTargets },
            TestNpcs.Place(Mod, 6, MissingBase.Record.ToFormKey(), Vector3.Zero, default) with { SpaceKey = SpaceWithoutTargets },
        ];
        var scene = TestScenes.Create(targets, otherModObjects, Shapes);
        var world = TestScenes.CreateWorld(targets, otherModObjects);

        var census = InvisibleOtherObjectCounter.Take(world, Shapes, scene.VisibleTargets);

        Assert.Equal(
            new[] { KeyValuePair.Create("MapMarkers", 1), KeyValuePair.Create("base not found", 1) },
            census.InvisibleByReason);
        Assert.Equal(1, census.PlacedNpcs);
        QueryEveryOtherModObjectOfTheSpace(scene, otherModObjects.Count);
        Assert.Equal(census.InvisibleByReason, InvisibleOtherObjectCounter.Take(world, Shapes, scene.VisibleTargets).InvisibleByReason);
    }

    /// <summary>What a too-close search asks, so the census can show it does not depend on those questions.</summary>
    private static void QueryEveryOtherModObjectOfTheSpace(ObjectCaches scene, int otherModObjectCount)
    {
        var area = OrientedBox.FromLocal(new Box(new Vector3(-100000), new Vector3(100000)), TestTargets.At(Vector3.Zero));
        scene.ObjectsThatCanCauseRemovals(Replacements.None(otherModObjectCount), NpcHandling.CountLikeObjects)
            .Overlapping(TestTargets.Space, area, new ObjectQueryScratch(), []);
    }
}
