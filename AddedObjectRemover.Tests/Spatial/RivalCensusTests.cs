using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Spatial;

public class RivalCensusTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Census.esp");
    private static readonly FormKey SpaceWithOnlyInvisibleTargets = new(TestTargets.TargetMod, 0x200);
    private static readonly FormKey SpaceWithoutTargets = new(TestTargets.TargetMod, 0x201);
    private static readonly BaseRef MissingBase = new(new FormKey(Mod, 0x7FF), typeof(IStaticGetter));

    private static readonly TestStatic Crate = new(
        new FormKey(Mod, 0x801), @"test\crate.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50), new Vector3(50))));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(Mod, "RivalCensusData", Crate);

    [Fact]
    public void CountsEveryInvisibleRivalOfTheSpacesWithAVisibleTarget()
    {
        List<TargetObject> targets =
        [
            TestTargets.Create(0, TestTargets.At(Vector3.Zero), Crate.Ref, TestTargets.Space),
            TestTargets.Create(1, TestTargets.At(Vector3.Zero), baseRef: null, SpaceWithOnlyInvisibleTargets),
        ];
        TargetLooks looks = new([ObjectVisibility.Visible, ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers)]);
        var farAway = new Vector3(50000, 0, 0);
        List<OtherObject> rivals =
        [
            TestShapes.Placed(Mod, 0, Crate.Ref, farAway),
            TestShapes.Placed(Mod, 1, MissingBase, farAway),
            TestShapes.Placed(Mod, 2, Crate.Ref, farAway) with { HasMapMarker = true },
            TestShapes.Placed(Mod, 3, MissingBase, Vector3.Zero) with { SpaceKey = SpaceWithOnlyInvisibleTargets },
            TestNpcs.Place(Mod, 4, MissingBase.FormKey, Vector3.Zero, default) with { SpaceKey = SpaceWithOnlyInvisibleTargets },
            TestShapes.Placed(Mod, 5, MissingBase, Vector3.Zero) with { SpaceKey = SpaceWithoutTargets },
            TestNpcs.Place(Mod, 6, MissingBase.FormKey, Vector3.Zero, default) with { SpaceKey = SpaceWithoutTargets },
        ];
        var scene = TestScenes.Create(targets, rivals, Shapes);

        var census = scene.Census(looks);

        Assert.Equal(
            new[] { KeyValuePair.Create("MapMarkers", 1), KeyValuePair.Create("base not found", 1) },
            census.InvisibleByReason);
        Assert.Equal(1, census.PlacedNpcs);
        QueryEveryRivalOfTheSpace(scene, rivals.Count);
        Assert.Equal(census.InvisibleByReason, scene.Census(looks).InvisibleByReason);
    }

    /// <summary>What a too-close search asks, so the census can show it does not depend on those questions.</summary>
    private static void QueryEveryRivalOfTheSpace(Scene scene, int rivalCount)
    {
        var area = new Box(new Vector3(-100000), new Vector3(100000));
        scene.ActiveRivals(Replacements.None(rivalCount), NpcHandling.CountLikeObjects)
            .Overlapping(TestTargets.Space, area, new SpatialQueryScratch(), []);
    }
}
