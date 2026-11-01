using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The guards of the placed records that go back where the cleaned mod was removed, over in-memory scenes: base game rocks the
/// cleaned mod moved away from a clearing whose only object of the cleaned mod was removed, each checked at the place it goes back to
/// and, for what rests on it, where it stands now.
/// </summary>
public sealed class RestoreWhereRemovedGuardTests
{
    private const float SearchRadius = 1000f;
    private const float TooCloseDistance = 10f;
    private const float RockHalfSize = 20f;
    private const float TouchGap = 1f;

    /// <summary>A collision layer number far beyond the layers the game defines, so no collision layer record carries it.</summary>
    private const uint NumberNoLayerHas = 9999;

    /// <summary>A rock's zone reaches its half size plus the too-close distance from its centre.</summary>
    private const float ZoneReach = RockHalfSize + TooCloseDistance;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreGuardShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreguards\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    private static readonly TestStatic Lantern = new(new FormKey(ShapeMod, 0x802), @"test\restoreguards\lantern.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-3, -3, 0), new Vector3(3, 3, 6))));
    /// <summary>27 cubic units, under the share of the removed house's box that may still stand in a cleared area.</summary>
    private static readonly TestStatic Pebble = new(new FormKey(ShapeMod, 0x805), @"test\restoreguards\pebble.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-1.5f, -1.5f, 0), new Vector3(1.5f, 1.5f, 3))));

    private static readonly Box RockBounds = new(new Vector3(-RockHalfSize), new Vector3(RockHalfSize));

    /// <summary>An invisible wall: a mesh with collision that stops characters and nothing to render.</summary>
    private static readonly TestStatic CollisionWall = new(new FormKey(ShapeMod, 0x803), @"test\restoreguards\collisionwall.nif", [], NiflySharp.Enums.SkyrimLayer.SKYL_STATIC, RockBounds);

    /// <summary>An invisible marker: a mesh with neither collision nor anything to render.</summary>
    private static readonly TestStatic EmptyMarker = new(new FormKey(ShapeMod, 0x804), @"test\restoreguards\emptymarker.nif", [], Collision: null, RockBounds);

    /// <summary>A base without anything to render whose own box is 4 units wide, for primitive boxes far larger than it.</summary>
    private static readonly TestStatic SmallBase = new(new FormKey(ShapeMod, 0x806), @"test\restoreguards\smallbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)));

    /// <summary>As <see cref="SmallBase"/>, with the engine's marker flag.</summary>
    private static readonly TestStatic MarkerBase = new(new FormKey(ShapeMod, 0x807), @"test\restoreguards\markerbase.nif", [], Collision: null, new Box(new Vector3(-2), new Vector3(2)), IsMarker: true);

    /// <summary>An activator without a model, such as a trigger box the base game places as a primitive.</summary>
    private static readonly BaseKey TriggerActivator = new(new FormKey(ShapeMod, 0x808).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static readonly BaseKey AcousticSpaceBase = new(new FormKey(ShapeMod, 0x809).ToRecordKey(), BaseLinkKind.PlaceableObject);

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(
        WithActivatorAndAcousticSpace(TestCollisionLayers.AddTo(new SkyrimMod(ShapeMod, SkyrimRelease.SkyrimSE))),
        "RestoreGuardData",
        Block, Lantern, CollisionWall, EmptyMarker, Pebble, SmallBase, MarkerBase));
    private static readonly BaseKey Marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName Later = LaterMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []);

    private static readonly WhereRemovedRestoreSettings Settings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: false,
        Navmeshes: false,
        new ClearedArea(SearchRadius, MaxStandingPercent: 0.1, ClearedAreaMeasure.ObjectSize),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never),
        new ChangedTerrainSettings(
            On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, 8f, CountTerrainTypeChanges: false, 0,
            CountWaterHeightChanges: false, 0f, CountWaterAddedOrRemoved: false, 0, AreaBySize: false, 0f, AreaByDistance: false, 0f));

    private static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap, SupportLostFraction: 0.5f);

    private static readonly Vector3 OldPlace = Vector3.Zero;
    private static readonly Vector3 PlaceNow = new(600, 0, 0);
    private static readonly RecordKey ChangedNavmeshKey = new FormKey(TestTargets.TargetMod, 0x600).ToRecordKey();
    private static readonly RecordKey BaseGameNavmeshKey = new FormKey(BaseGameMod, 0x600).ToRecordKey();

    [Theory]
    [InlineData(ZoneReach - 5f, false)]
    [InlineData(ZoneReach + 5f, true)]
    public void ARockGoingBackTooCloseToALaterModsObjectThatStaysIsRefused(float distance, bool restored)
    {
        var crate = LaterObject(0, Block.Base, OldPlace + new Vector3(distance, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [crate, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored, judged.IsRestored);
        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), judged.Refused);
    }

    /// <summary>The base game's crate stood next to the rock before the cleaned mod moved the rock, so it keeps no rock from going back.</summary>
    [Fact]
    public void AnObjectThatStoodThereWithoutTheCleanedModRefusesNothing()
    {
        var crate = TestShapes.Placed(BaseGameMod, 1, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [crate, RockNow(0, PlaceNow)] };

        Assert.True(Assert.Single(Plan(scene).Objects).IsRestored);
    }

    /// <summary>The cleaned mod moved rock B onto rock A's old place and rock A elsewhere; both go back, so B's place now does not keep A.</summary>
    [Fact]
    public void RocksGoingBackTogetherAreNotCheckedAgainstEachOther()
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, new Vector3(500, 0, 0), OldPlace + new Vector3(5, 0, 0))],
            Others = [RockNow(0, PlaceNow), RockNow(1, OldPlace + new Vector3(5, 0, 0))],
        };

        Assert.All(Plan(scene).Objects, judged => Assert.True(judged.IsRestored));
    }

    /// <summary>As above, but rock B stands in a navmesh that stays changed at its old place, so it stays on rock A's old place and keeps A too.</summary>
    [Fact]
    public void ARockRefusedStaysWhereItStandsAndKeepsARockThatWouldGoBackOntoIt()
    {
        var bOldPlace = new Vector3(500, 0, 0);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, bOldPlace, OldPlace + new Vector3(5, 0, 0))],
            Others = [RockNow(0, PlaceNow), RockNow(1, OldPlace + new Vector3(5, 0, 0))],
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(bOldPlace))],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        var result = Plan(scene);

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, RockKey(1), Failure: null), new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null)],
            result.Objects.Select(judged => judged.Refused));
    }

    [Theory]
    [InlineData(RockHalfSize - 5f, false)]
    [InlineData(RockHalfSize + 5f, true)]
    public void ARockThatWouldHoldAMarkerOfTheCleanedModThatStaysIsRefused(float distance, bool restored)
    {
        var marker = TestTargets.Create(1, TestTargets.At(OldPlace + new Vector3(distance, 0, 0)), Marker, TestTargets.Space);
        var scene = new Scene { Targets = [RemovedHouse(), marker], Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.HoldsInvisibleObjectThatStays, marker.Key, Failure: null), judged.Refused);
    }

    /// <summary>A triangle at the given height of a navmesh the cleaned mod added; the rock's box reaches from -20 to 20.</summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(RockHalfSize + 1f, true)]
    public void ARockGoingBackIntoANavmeshThatStaysChangedIsRefused(float height, bool restored)
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(OldPlace + new Vector3(0, 0, height)))],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null), judged.Refused);
    }

    [Fact]
    public void ANavmeshTheCleanedModDidNotChangeRefusesNothing()
    {
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
            Navmesh = [new NavmeshWorldTriangle(BaseGameNavmeshKey, FlatTriangle(OldPlace))],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        Assert.True(Assert.Single(Plan(scene).Objects).IsRestored);
    }

    /// <summary>The cleaned mod moved a base game invisible wall, or an invisible marker without collision, out of a navmesh it added.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnInvisibleObjectWithCollisionGoingBackIntoANavmeshThatStaysChangedIsRefused(bool collision, bool restored)
    {
        var model = collision ? CollisionWall.Base : EmptyMarker.Base;
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow, model)],
            Others = [RockNow(0, PlaceNow, model)],
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(OldPlace))],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null), judged.Refused);
    }

    /// <summary>
    /// The cleaned mod moved a base game primitive box 40 units wide, on a base without a visible mesh whose own box is 4 units wide,
    /// out of a navmesh it added; the navmesh's triangle lies 15 units above the box's centre. The collision layer the reference stores
    /// decides whether characters run into it, by the load order's collision layer records: the static and the collision box layers
    /// collide with the character controller's, the trigger layer is a trigger and the navmesh cut layer collides with nothing. With no
    /// layer stored, or one no record has, the layer is not known and the box counts.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData((uint)CollisionLayer.Static, false)]
    [InlineData((uint)CollisionLayer.CollisionBox, false)]
    [InlineData((uint)CollisionLayer.Trigger, true)]
    [InlineData((uint)CollisionLayer.NavCut, false)]
    [InlineData((uint)CollisionLayer.Ward, false)]
    [InlineData(NumberNoLayerHas, false)]
    public void APrimitiveGoingBackIntoANavmeshThatStaysChangedIsRefusedUnlessItsCollisionLayerLetsCharactersThrough(uint? layer, bool restored) =>
        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null), PrimitiveGoingBack(SmallBase.Base, layer).Refused);

    /// <summary>As above, with a base that carries the engine's marker flag, whose own box is empty.</summary>
    [Theory]
    [InlineData((uint)CollisionLayer.CollisionBox, false)]
    [InlineData((uint)CollisionLayer.NavCut, false)]
    [InlineData((uint)CollisionLayer.Trigger, true)]
    public void APrimitiveOnAMarkerBaseGoingBackIntoANavmeshThatStaysChangedIsJudgedByItsOwnBoxAndCollisionLayer(uint layer, bool restored) =>
        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null), PrimitiveGoingBack(MarkerBase.Base, layer).Refused);

    /// <summary>
    /// A primitive that stores no collision layer has the default layer of its kind: activators (trigger boxes) and acoustic spaces let
    /// characters through, and so does a room marker, portal or multibound primitive of any base. A collision marker's default
    /// layer is the collision box, so it blocks.
    /// </summary>
    [Theory]
    [InlineData(PrimitiveKind.Activator, true)]
    [InlineData(PrimitiveKind.AcousticSpace, true)]
    [InlineData(PrimitiveKind.CullingVolumeOnMarkerBase, true)]
    [InlineData(PrimitiveKind.CollisionMarker, false)]
    public void APrimitiveStoringNoCollisionLayerGoingBackIntoANavmeshThatStaysChangedIsRefusedOnlyWhenItsKindsDefaultLayerBlocks(PrimitiveKind kind, bool restored)
    {
        var (model, cullingVolume) = kind switch
        {
            PrimitiveKind.Activator => (TriggerActivator, false),
            PrimitiveKind.AcousticSpace => (AcousticSpaceBase, false),
            PrimitiveKind.CullingVolumeOnMarkerBase => (MarkerBase.Base, true),
            _ => (MarkerBase.Base, false),
        };

        var judged = PrimitiveGoingBack(model, layer: null, cullingVolume);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null), judged.Refused);
    }

    /// <summary>The kinds of primitive reference whose default collision layer the guard tells apart.</summary>
    public enum PrimitiveKind { Activator, AcousticSpace, CullingVolumeOnMarkerBase, CollisionMarker }

    /// <summary>A trigger box that stores a collision layer is judged by it whatever its kind: an activator on the static layer blocks.</summary>
    [Fact]
    public void AnActivatorPrimitiveStoringACollisionLayerThatStopsCharactersIsRefused() =>
        Assert.Equal(
            new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, ChangedNavmeshKey, Failure: null),
            PrimitiveGoingBack(TriggerActivator, (uint)CollisionLayer.Static).Refused);

    /// <summary>The scene of the primitive tests: the box goes back to <see cref="OldPlace"/>, under the navmesh's triangle.</summary>
    private static PlacedChangeJudged PrimitiveGoingBack(BaseKey model, uint? layer, bool cullingVolume = false)
    {
        var primitive = TestPlacedRecords.PlainReference with
        {
            IsPrimitive = true, PrimitiveBounds = new Vector3(2 * RockHalfSize), CollisionLayer = layer, IsCullingVolume = cullingVolume,
        };
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow, model, primitive)],
            Others = [RockNow(0, PlaceNow, model) with { IsPrimitive = true }],
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(OldPlace + new Vector3(0, 0, RockHalfSize - 5f)))],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };
        return Assert.Single(Plan(scene).Objects);
    }

    /// <summary>The base game's crate stood 100 units above the rock's old place; the patch pulls it down next to it, or leaves it.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnObjectThePatchPullsDownNextToTheOldPlaceRefusesTheRock(bool pulledDown, bool restored)
    {
        const float Drop = 100f;
        var crate = TestShapes.Placed(BaseGameMod, 1, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, Drop)) with { Id = new OtherId(0) };
        var pulled = new OtherModsObjectOutcome(crate, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop, ReferenceRoles.None);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [crate, RockNow(0, PlaceNow)],
            OtherMods = pulledDown ? new OtherModsObjectsResult(StepOutcome.Performed, [pulled], [], [], Failure: null) : OtherModsObjectsResult.SwitchedOff,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), judged.Refused);
    }

    /// <summary>A pebble of the cleaned mod's run that stays, too small to keep the area from being cleared, lies next to the rock's old place.</summary>
    [Fact]
    public void AnObjectOfTheCleanedModThatStaysRefusesARockGoingBackNextToIt()
    {
        var pebble = TestTargets.Create(1, TestTargets.At(OldPlace + new Vector3(ZoneReach, 0, 0)), Pebble.Base, TestTargets.Space);
        var scene = new Scene { Targets = [RemovedHouse(), pebble], Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, pebble.Key, Failure: null), judged.Refused);
    }

    /// <summary>A pebble of the cleaned mod's run that stays lies on the rock where the cleaned mod put it.</summary>
    [Fact]
    public void ARockAnObjectOfTheCleanedModThatStaysRestsOnIsRefused()
    {
        var pebble = TestTargets.Create(1, TestTargets.At(PlaceNow + new Vector3(0, 0, RockHalfSize)), Pebble.Base, TestTargets.Space);
        var scene = new Scene { Targets = [RemovedHouse(), pebble], Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(new PlacedRefusal(PlacedGuard.CarriesObjectThatStays, pebble.Key, Failure: null), judged.Refused);
    }

    /// <summary>
    /// Rock A lies on rock B where the cleaned mod put them. A later mod's crate refuses A, which therefore stays on B: the next pass
    /// refuses B, which would leave A floating.
    /// </summary>
    [Fact]
    public void ARockCarryingARockRefusedInAnEarlierPassIsRefused()
    {
        var aNow = PlaceNow + new Vector3(0, 0, 2 * RockHalfSize);
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, aNow), Rock(1, new Vector3(-300, 0, 0), PlaceNow)],
            Others = [RockNow(0, aNow), RockNow(1, PlaceNow), crate],
        };

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), new PlacedRefusal(PlacedGuard.CarriesObjectThatStays, RockKey(0), Failure: null)],
            Plan(scene).Objects.Select(judged => judged.Refused));
    }

    /// <summary>A later mod's lantern stands on top of the rock where the cleaned mod put it, or a little above it.</summary>
    [Theory]
    [InlineData(0f, false)]
    [InlineData(5f, true)]
    public void ARockALaterModsLanternRestsOnWhereItStandsNowIsRefused(float gapBelowLantern, bool restored)
    {
        var lantern = LaterObject(0, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize + gapBelowLantern));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.CarriesObjectThatStays, lantern.Key, Failure: null), judged.Refused);
    }

    /// <summary>Rock B lies on rock A where the cleaned mod put them; both go back, so nothing is left floating.</summary>
    [Fact]
    public void ARockRestingOnAnotherThatGoesBackTooKeepsNeither()
    {
        var bNow = PlaceNow + new Vector3(0, 0, 2 * RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, new Vector3(-300, 0, 0), bNow)],
            Others = [RockNow(0, PlaceNow), RockNow(1, bNow)],
        };

        Assert.All(Plan(scene).Objects, judged => Assert.True(judged.IsRestored));
    }

    [Fact]
    public void TheGuardsGiveTheSameResultsWhateverTheNumberOfWorkers()
    {
        var one = Plan(ManyRocks(workers: 1));
        var eight = Plan(ManyRocks(workers: 8));

        Assert.Equal(one.Objects.Select(Describe), eight.Objects.Select(Describe));
        Assert.Contains(one.Objects, judged => judged.IsRestored);
        Assert.Equal(
            [PlacedGuard.TooCloseToWhatStays, PlacedGuard.HoldsInvisibleObjectThatStays, PlacedGuard.StandsInChangedNavmesh, PlacedGuard.CarriesObjectThatStays],
            one.Objects.Select(judged => judged.Refused?.Guard).OfType<PlacedGuard>().Distinct().Order());
    }

    [Fact]
    public void TheGuardsGiveTheSameResultsWhateverTheOrderOfTheRecordsAndObjectsRead()
    {
        var random = new Random(5);
        var scene = ManyRocks(workers: 1);
        var shuffled = scene with { Rocks = [.. scene.Rocks.OrderBy(_ => random.Next())], Others = [.. scene.Others.OrderBy(_ => random.Next())] };

        Assert.Equal(Plan(scene).Objects.Select(Describe), Plan(shuffled).Objects.Select(Describe));
    }

    /// <summary>
    /// Forty rocks in a row, each moved by the cleaned mod to a second row; some go back next to a later mod's crate, around a
    /// marker of the cleaned mod, into a navmesh the cleaned mod added, or carry a lantern where they stand, and some were moved onto
    /// the next rock's old place.
    /// </summary>
    private static Scene ManyRocks(int workers)
    {
        const int Count = 40;
        const float Spacing = 100f;
        var targets = Enumerable.Range(0, 9).Select(index => Own(index, new Vector3(index * 500f, 300, 0))).ToList();
        var rocks = new List<RecordChangedByCleanedMod>();
        var others = new List<OtherObject>();
        for (var index = 0; index < Count; index++)
        {
            var oldPlace = new Vector3(index * Spacing, 0, 0);
            var now = index % 7 == 0 ? oldPlace + new Vector3(Spacing + 5, 0, 0) : new Vector3(index * Spacing, 2000, 0);
            rocks.Add(Rock(index, oldPlace, now));
            others.Add(RockNow(index, now));
            if (index % 4 == 1) others.Add(LaterObject(index, Block.Base, oldPlace + new Vector3(0, ZoneReach - 5f, 0)));
            if (index % 5 == 2) targets.Add(TestTargets.Create(targets.Count, TestTargets.At(oldPlace + new Vector3(5, 0, 0)), Marker, TestTargets.Space));
            if (index % 6 == 3) others.Add(LaterObject(Count + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize)));
        }
        return new Scene
        {
            Targets = targets,
            Removed = [.. Enumerable.Range(0, 9)],
            Rocks = rocks,
            Others = others,
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(new Vector3(1000, 0, 0)))],
            ChangedNavmeshes = [ChangedNavmeshKey],
            Workers = workers,
        };
    }

    private static SkyrimMod WithActivatorAndAcousticSpace(SkyrimMod mod)
    {
        mod.Activators.Add(new Mutagen.Bethesda.Skyrim.Activator(TriggerActivator.Record.ToFormKey(), SkyrimRelease.SkyrimSE));
        mod.AcousticSpaces.Add(new AcousticSpace(AcousticSpaceBase.Record.ToFormKey(), SkyrimRelease.SkyrimSE));
        return mod;
    }

    private static string Describe(PlacedChangeJudged judged) => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored}";

    /// <summary>The objects of one scene; the cleaned mod's objects listed in <see cref="Removed"/> are removed.</summary>
    private sealed record Scene
    {
        public List<TargetObject> Targets { get; init; } = [RemovedHouse()];
        public int[] Removed { get; init; } = [0];
        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The other plugins' objects that stand now, numbered in this order.</summary>
        public List<OtherObject> Others { get; init; } = [];

        public List<NavmeshWorldTriangle> Navmesh { get; init; } = [];
        public List<RecordKey> ChangedNavmeshes { get; init; } = [];

        /// <summary>What became of other mods' objects left without support; their ids are their places in <see cref="Others"/>.</summary>
        public OtherModsObjectsResult OtherMods { get; init; } = OtherModsObjectsResult.SwitchedOff;

        public int Workers { get; init; } = 1;
    }

    /// <summary>The cleaned mod's only visible object near the old places, which the rounds removed.</summary>
    private static TargetObject RemovedHouse() => Own(0, new Vector3(200, 300, 0));

    private static TargetObject Own(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space);

    private static RecordKey RockKey(int index) => new FormKey(BaseGameMod, 0x900 + (uint)index).ToRecordKey();

    /// <summary>A base game rock the cleaned mod moved from <paramref name="oldPlace"/> to <paramref name="now"/>, whose position goes back.</summary>
    /// <param name="model">The rock's base; a block when null.</param>
    /// <param name="reference">What its winning version says about the reference itself; a plain reference when null.</param>
    private static RecordChangedByCleanedMod Rock(int index, Vector3 oldPlace, Vector3 now, BaseKey? model = null, ReferenceFacts? reference = null) =>
        new(
            RockKey(index),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            new EulerPlacement(now, Vector3.Zero, 1f),
            model ?? Block.Base,
            reference ?? TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(oldPlace, Vector3.Zero, 1f), model ?? Block.Base) with { Undo = PlacedParts.Position },
            Target: null,
            LinkedFromCleanedMod: false,
            PresenceBefore: default);

    /// <summary>The rock where the cleaned mod put it: its override is a change in the world without the cleaned mod.</summary>
    /// <param name="model">The rock's base; a block when null.</param>
    private static OtherObject RockNow(int index, Vector3 now, BaseKey? model = null) =>
        TestShapes.Placed(BaseGameMod, index, model ?? Block.Base, now) with { ChangeWithoutCleanedMod = new ObjectChange(TestTargets.TargetName, BaseGame) };

    private static OtherObject LaterObject(int index, BaseKey model, Vector3 position) =>
        TestShapes.Placed(LaterMod, index, model, position) with { ChangeWithoutCleanedMod = new ObjectChange(Later, AcceptedFrom: null) };

    private static MeshTriangle FlatTriangle(Vector3 centre) =>
        new(centre + new Vector3(-100, -100, 0), centre + new Vector3(100, -100, 0), centre + new Vector3(0, 100, 0));

    private static NavmeshChangedByCleanedMod ChangedNavmesh(RecordKey navmesh)
    {
        var cell = new FormKey(TestTargets.TargetMod, 0x601).ToRecordKey();
        return new NavmeshChangedByCleanedMod(
            navmesh,
            TestTargets.Space,
            cell,
            TestTargets.TargetName,
            NavmeshCase.Added,
            new PartOutcome.Unchanged(),
            new PartOutcome.Undo(),
            new PartOutcome.Unchanged(),
            Earlier: null,
            new NavmeshShape(TestTargets.TargetName, cell, IsDeleted: false, [], [], [], [], MapEntry: null),
            ReplacedBy: [],
            Neighbours: [],
            Doors: [],
            NamedInPreferredPaths: false,
            WinningMapMod: null,
            CellDeletedInAVersion: false,
            LaterWinner: null,
            WinningMapEntry: null);
    }

    private static RestoreWhereRemovedResult Plan(Scene scene)
    {
        ImmutableArray<TargetObject> targets = [.. scene.Targets];
        var collected = TestScenes.CreateWorld(targets, [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })]) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedNavmeshes = [.. scene.ChangedNavmeshes.Select(ChangedNavmesh)],
        };
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), scene.Workers);
        ImmutableArray<NavmeshWorldTriangle> navmesh = [.. scene.Navmesh];
        return WhereRemovedRestorer.Plan(
            new(
                collected,
                DecisionsRemoving(targets, scene.Removed),
                scene.OtherMods,
                TestScenes.Create(targets, [], Shapes).VisibleTargets,
                rules.WorldCaches,
                rules.TooCloseRule(),
                rules.HoldersOfObjectsThatStay,
                TestNavmeshCheck.NoFault,
                rules.Support,
                Shapes,
                new NavmeshIndex(bucket => bucket.Grid is null && !navmesh.IsEmpty ? [.. navmesh] : null),
                TestLandPlugin.Flat,
                Plugins,
                TestTargets.TargetName,
                new Execution(scene.Workers),
                Settings));
    }

    private static RemovalDecisions DecisionsRemoving(ImmutableArray<TargetObject> targets, int[] removed) =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(targets, [], TestTargets.References(targets.Length), TestBaseFacts.Unresolved), targets.Length)
            .Apply(RoundKind.TooClose, [.. removed.Select(index => new ProposedRemoval(new TargetId(index), new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);
}
