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

    /// <summary>The default limit by size.</summary>
    private const double MaxStandingSizePercent = 1;

    /// <summary>By number a pebble that stays beside the removed house, one of two, may stand; two of three may not.</summary>
    private const double MaxStandingCountPercent = 60;

    /// <summary>A collision layer number far beyond the layers the game defines, so no collision layer record carries it.</summary>
    private const uint NumberNoLayerHas = 9999;

    /// <summary>A rock's zone reaches its half size plus the too-close distance from its centre.</summary>
    private const float ZoneReach = RockHalfSize + TooCloseDistance;

    private static readonly ModKey ShapeMod = ModKey.FromNameAndExtension("RestoreGuardShapes.esp");
    private static readonly TestStatic Block = new(new FormKey(ShapeMod, 0x801), @"test\restoreguards\block.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-RockHalfSize), new Vector3(RockHalfSize))));
    /// <summary>A rock twice as wide as <see cref="Block"/>.</summary>
    private static readonly TestStatic Boulder = new(new FormKey(ShapeMod, 0x80A), @"test\restoreguards\boulder.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-2 * RockHalfSize), new Vector3(2 * RockHalfSize))));
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
        Block, Boulder, Lantern, CollisionWall, EmptyMarker, Pebble, SmallBase, MarkerBase));
    private static readonly BaseKey Marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));

    private static readonly ModKey BaseGameMod = ModKey.FromNameAndExtension("Base.esm");
    private static readonly ModKey LaterMod = ModKey.FromNameAndExtension("Later.esp");
    private static readonly PluginName BaseGame = BaseGameMod.ToPluginName();
    private static readonly PluginName Later = LaterMod.ToPluginName();
    private static readonly PluginName[] PluginList = [BaseGame, TestTargets.TargetName, TestTargets.PatchName, Later];
    private static readonly LoadOrderPlugins Plugins = TestLoadOrderPlugins.Of(
        new KnownPluginNames(PluginList), TestTargets.PatchName, [.. PluginList.Select(plugin => new PluginListing(plugin, true, []))], PluginList, []);
    private static readonly ModTrustTable Trust = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [], baseGame: [BaseGame]);

    /// <summary>
    /// With rough boxes an object is too close only when its centre lies in the checked object's box grown by the distance, so two
    /// same-size blocks side by side or stacked never are; the scenes are built with that in mind.
    /// </summary>
    private static readonly WhereRemovedRestoreSettings Settings = new(
        OtherModsObjects: true,
        Terrain: false,
        Cells: false,
        Navmeshes: false,
        new ClearedArea(SearchRadius, MaxStandingSizePercent, MaxStandingCountPercent),
        TerrainEdgeBlendDistance: 0f,
        HeightChangeTolerance: 8f,
        WaterHeightChangeTolerance: 8f,
        new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: TooCloseDistance, ZoneShape.BoundingBox, NpcHandling.Never));

    private static readonly OtherModsObjectsSettings Support = new(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap, SupportLostFraction: 0.5f);

    private static readonly Vector3 OldPlace = Vector3.Zero;
    private static readonly Vector3 PlaceNow = new(600, 0, 0);

    /// <summary>Where the cleaned mod put a rock, beyond the area around its old place.</summary>
    private static readonly Vector3 FarPlaceNow = new(3 * SearchRadius, 0, 0);
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

    /// <summary>
    /// A later mod's block of the rock's size stands 5 units beside the rock's old place. With exact shapes, the default, their meshes
    /// come within the distance, so the rock is refused; with rough boxes the block's centre lies beyond the rock's grown box, so the
    /// rock goes back.
    /// </summary>
    [Theory]
    [InlineData(ZoneShape.ObjectShape, false)]
    [InlineData(ZoneShape.BoundingBox, true)]
    public void ARockGoingBackBesideASameSizeBlockIsTooCloseByExactShapes(ZoneShape zone, bool restored)
    {
        var block = LaterObject(0, Block.Base, OldPlace + new Vector3(2 * RockHalfSize + 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [block, RockNow(0, PlaceNow)], Zone = zone };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? (true, null) : (false, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, block.Key, Failure: null)), (judged.IsRestored, judged.Refused));
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

    /// <summary>A marker of the cleaned mod that stays would lie inside the rock where it goes back: an invisible object it holds refuses nothing.</summary>
    [Fact]
    public void ARockThatWouldHoldAMarkerOfTheCleanedModThatStaysGoesBack()
    {
        var marker = TestTargets.Create(1, TestTargets.At(OldPlace + new Vector3(RockHalfSize - 5f, 0, 0)), Marker, TestTargets.Space);
        var scene = new Scene { Targets = [RemovedHouse(), marker], Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
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

    /// <summary>
    /// A later mod's lantern stands next to the rock's old place, a gap wider than the touch gap away, so the rock does not rest on
    /// it; the patch removes it, or leaves it.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AnObjectThePatchRemovesNextToTheOldPlaceRefusesNothing(bool removedByThePatch, bool restored)
    {
        var crate = LaterObject(0, Lantern.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var removal = new OtherModsObjectOutcome(crate, OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [crate, RockNow(0, PlaceNow)],
            OtherMods = removedByThePatch ? new OtherModsObjectsResult(StepOutcome.Performed, [removal], [], [], Failure: null) : OtherModsObjectsResult.SwitchedOff,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(restored ? null : new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), judged.Refused);
    }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredBesideTheOldPlaceRefusesNothing"/>, with a later mod's crate also next to the rock's
    /// old place: only the restored object is left out for a rock that ends in its earlier shape and place, so the later crate keeps it out.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredLeavesOutNoOtherObjectThatStays()
    {
        var laterCrate = LaterObject(1, Block.Base, OldPlace - new Vector3(ZoneReach - 5f, 0, 0));
        var scene = RestoredCrateBesideRock(Rock(0, OldPlace, PlaceNow)) with { Others = [RockNow(0, PlaceNow), laterCrate] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal(new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, laterCrate.Key, Failure: null), judged.Refused);
    }

    /// <summary>
    /// The cleaned mod moved a base game crate too; a removal step put it back where it stood before, next to the rock's old place.
    /// The two stood together there before the cleaned mod.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredBesideTheOldPlaceRefusesNothing()
    {
        var scene = RestoredCrateBesideRock(Rock(0, OldPlace, PlaceNow));

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// The cleaned mod gave a base game crate another base; a removal step put it back with its earlier base, next to the rock's old
    /// place. The two stood together there before the cleaned mod.
    /// </summary>
    [Fact]
    public void AnObjectWhoseEarlierBaseARemovalStepRestoredBesideTheOldPlaceRefusesNothing()
    {
        var replacedCrate = MovedCrate() with { Origin = TargetOrigin.Replaced, Base = Lantern.Base };
        var scene = RestoredCrateBesideRock(Rock(0, OldPlace, PlaceNow)) with
        {
            Targets = [RemovedHouse(), replacedCrate],
            EarlierBases = new Dictionary<int, BaseKey> { [1] = Block.Base },
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredBesideTheOldPlaceRefusesNothing"/>, but a later plugin made the rock larger, so it goes
    /// back to its old place at that size. The next patcher reads the crate a removal step put back as support only, never as a cause,
    /// so it keeps no rock out, whatever shape the rock keeps.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredRefusesNoRockThatKeepsTheSizeALaterPluginGaveIt()
    {
        const float LaterScale = 2f;
        var moved = Rock(0, OldPlace, PlaceNow);
        var rock = moved with
        {
            WhereItIsNow = new EulerPlacement(PlaceNow, Vector3.Zero, LaterScale),
            Earlier = moved.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) },
        };
        var scene = RestoredCrateBesideRock(rock, ZoneReach + 5f);

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredRefusesNoRockThatKeepsTheSizeALaterPluginGaveIt"/>, but a later plugin gave the rock a
    /// larger model, which it keeps where it goes back.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredRefusesNoRockThatKeepsTheModelALaterPluginGaveIt()
    {
        var moved = Rock(0, OldPlace, PlaceNow);
        var rock = moved with
        {
            BaseNow = Boulder.Base,
            Earlier = moved.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.BaseAndData, Later) },
        };
        var scene = RestoredCrateBesideRock(rock, ZoneReach + 5f) with { Others = [RockNow(0, PlaceNow, Boulder.Base)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredBesideTheOldPlaceRefusesNothing"/>, but a later plugin gave the rock a sound radius,
    /// which it keeps where it goes back: the radius does not shape the rock, so it still ends as it stood beside the crate.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredRefusesNoRockThatKeepsOnlyReferenceDataALaterPluginGaveIt()
    {
        const float LaterRadius = 512f;
        var moved = Rock(0, OldPlace, PlaceNow, reference: TestPlacedRecords.PlainReference with { ReferenceRadius = LaterRadius });
        var rock = moved with
        {
            Earlier = moved.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.BaseAndData, Later) },
        };
        var scene = RestoredCrateBesideRock(rock);

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    public enum LaterShapeChange { Rotation, Scale, PrimitiveBounds, CollisionLayer }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredRefusesNoRockThatKeepsTheSizeALaterPluginGaveIt"/>, but a later plugin changed another
    /// part that shapes the rock, by just more than the rounding of plugins that re-save a record, and the rock keeps it.
    /// </summary>
    [Theory]
    [InlineData(LaterShapeChange.Rotation)]
    [InlineData(LaterShapeChange.Scale)]
    [InlineData(LaterShapeChange.PrimitiveBounds)]
    [InlineData(LaterShapeChange.CollisionLayer)]
    public void AnObjectARemovalStepRestoredRefusesNoRockThatKeepsAShapeALaterPluginGaveIt(LaterShapeChange change)
    {
        var scene = RestoredCrateBesideRock(RockKeeping(change));

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>The rock as it goes back to its old place, keeping the change a later plugin made, just beyond the rounding.</summary>
    private static RecordChangedByCleanedMod RockKeeping(LaterShapeChange change)
    {
        const float beyond = 2f;
        var earlierPrimitive = TestPlacedRecords.PlainReference with { IsPrimitive = true, PrimitiveBounds = new Vector3(100f) };
        var (part, placementNow, referenceNow) = change switch
        {
            LaterShapeChange.Rotation => (PlacedParts.Rotation, new EulerPlacement(PlaceNow, new Vector3(0, 0, beyond * ResavedPlacedParts.Tolerances.Rotation), 1f), earlierPrimitive),
            LaterShapeChange.Scale => (PlacedParts.Scale, new EulerPlacement(PlaceNow, Vector3.Zero, 1f + beyond * ResavedPlacedParts.Tolerances.Scale), earlierPrimitive),
            LaterShapeChange.PrimitiveBounds => (PlacedParts.BaseAndData, new EulerPlacement(PlaceNow, Vector3.Zero, 1f),
                earlierPrimitive with { PrimitiveBounds = new Vector3(100f + beyond * ResavedPlacedParts.Tolerances.Position) }),
            LaterShapeChange.CollisionLayer => (PlacedParts.BaseAndData, new EulerPlacement(PlaceNow, Vector3.Zero, 1f), earlierPrimitive with { CollisionLayer = 15u }),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
        };
        var moved = Rock(0, OldPlace, PlaceNow, reference: referenceNow);
        return moved with
        {
            WhereItIsNow = placementNow,
            Earlier = moved.Earlier with
            {
                Reference = earlierPrimitive,
                LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(part, Later),
            },
        };
    }

    /// <summary>
    /// As <see cref="AnObjectARemovalStepRestoredBesideTheOldPlaceRefusesNothing"/>, but the cleaned mod stores the rock's rotation a
    /// full turn on and its scale re-saved: both lie within the rounding, so they do not go back, and the rock still ends as it stood.
    /// </summary>
    [Fact]
    public void AnObjectARemovalStepRestoredRefusesNoRockWhoseRotationAndScaleTheCleanedModOnlyReSaved()
    {
        const float ResavedScale = 0.99999994f;
        var rock = Rock(0, OldPlace, PlaceNow) with { WhereItIsNow = new EulerPlacement(PlaceNow, new Vector3(MathF.Tau, 0, 0), ResavedScale) };
        var scene = RestoredCrateBesideRock(rock);

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>Two pebbles of the cleaned mod stay beside the removed house: far under the limit by size, two of three by number.</summary>
    [Fact]
    public void TwoSmallPiecesThatStayKeepTheAreaByNumberAlone()
    {
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleAt(1, new Vector3(300, -300, 0)), PebbleAt(2, new Vector3(-300, -300, 0))],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((false, AreaVerdict.StillStanding), (judged.IsRestored, judged.OldPlace!.Verdict));
        Assert.True(judged.OldPlace.CountPercent > MaxStandingCountPercent && judged.OldPlace.SizePercent < MaxStandingSizePercent);
    }

    /// <summary>The house stays and two pebbles beside it were removed: one of three by number, nearly all by size.</summary>
    [Fact]
    public void OneLargePieceThatStaysKeepsTheAreaBySizeAlone()
    {
        var scene = new Scene
        {
            Targets = [RemovedHouse(), PebbleAt(1, new Vector3(300, -300, 0)), PebbleAt(2, new Vector3(-300, -300, 0))],
            Removed = [1, 2],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((false, AreaVerdict.StillStanding), (judged.IsRestored, judged.OldPlace!.Verdict));
        Assert.True(judged.OldPlace.CountPercent < MaxStandingCountPercent && judged.OldPlace.SizePercent > MaxStandingSizePercent);
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

    /// <summary>
    /// A pebble of the cleaned mod stays 70 units from the rock's old place, beyond the zones of the changed objects' settings. Its own
    /// collision group may check 100 units around it. The rock keeps a scale Later.esp set. Whether or not the removal steps believe
    /// Later.esp, the next patcher reads the rock the patch put back as support only, never as a cause, so the rock goes back.
    /// </summary>
    [Theory]
    [InlineData(true, VersionTrust.None)]
    [InlineData(true, VersionTrust.Full)]
    [InlineData(false, VersionTrust.None)]
    public void ARockGoingBackIsNoCauseForAnObjectOfTheCleanedModWhoseOwnGroupReachesIt(bool groupJudgesThePebble, VersionTrust laterTrust)
    {
        var pebble = PebbleAt(1, OldPlace + new Vector3(ZoneReach + 40f, 0, 0));
        var moved = Rock(0, OldPlace, PlaceNow);
        var rock = moved with { Earlier = moved.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Rocks = [rock],
            Others = [RockNow(0, PlaceNow)],
            StandingTooClose = groupJudgesThePebble ? Settings.TooClose with { DistanceInUnits = 100f } : null,
            LaterTrust = laterTrust,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A pebble of the cleaned mod's run that stays lies on the rock where the cleaned mod put it: the rock goes back and starts the
    /// follow-up of the pebble, which is removed with it, unless the follow-up mode is Nothing or the pebble is protected, which the
    /// follow-up keeps where it stands, as the removal steps do.
    /// </summary>
    [Theory]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, false, true)]
    [InlineData(FollowUpRemovalMode.EverythingTouchingIt, false, true)]
    [InlineData(FollowUpRemovalMode.Nothing, false, false)]
    [InlineData(FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, false)]
    public void ARockAnObjectOfTheCleanedModRestsOnGoesBackAndStartsItsFollowUp(FollowUpRemovalMode mode, bool pebbleProtected, bool removed)
    {
        var pebble = TestTargets.Create(1, TestTargets.At(PlaceNow + new Vector3(0, 0, RockHalfSize)), Pebble.Base, TestTargets.Space);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = pebbleProtected ? [] : [1],
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow)],
            FollowUp = TestPlacedGuardRules.DefaultFollowUp with { Mode = mode },
        };

        var result = Plan(scene);
        var judged = Assert.Single(result.Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
        Assert.Equal(
            removed ? [(pebble.Id, judged.Change.Key.ToString())] : [],
            result.FollowUp.Targets.Select(followed => (followed.Target, string.Join(", ", followed.StartedBy))));
        Assert.All(result.FollowUp.Targets, followed => Assert.Null(followed.RestoredTo));
    }

    /// <summary>
    /// A pebble of the cleaned mod's run that stays touches two rocks the cleaned mod moved: both go back, and the pebble's follow-up
    /// names the first rock it touches, as the also-remove reason of a removal step names the first object it touched.
    /// </summary>
    [Fact]
    public void TwoRocksAnObjectOfTheCleanedModTouchesBothStartItsFollowUp()
    {
        var secondNow = PlaceNow + new Vector3(2 * RockHalfSize, 0, 0);
        var pebble = TestTargets.Create(1, TestTargets.At(PlaceNow + new Vector3(RockHalfSize, 0, RockHalfSize)), Pebble.Base, TestTargets.Space);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = [1],
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, new Vector3(-300, 0, 0), secondNow)],
            Others = [RockNow(0, PlaceNow), RockNow(1, secondNow)],
            FollowUp = TestPlacedGuardRules.DefaultFollowUp with { Mode = FollowUpRemovalMode.EverythingTouchingIt },
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
        var followed = Assert.Single(result.FollowUp.Targets);
        Assert.Equal((pebble.Id, result.Objects[0].Change.Key), (followed.Target, Assert.Single(followed.StartedBy)));
    }

    /// <summary>
    /// Rock A lies on rock B where the cleaned mod put them. A later mod's crate refuses A, which therefore stays on B. A is no object
    /// of the cleaned mod, so B goes back from under it (decisions 31 and 32).
    /// </summary>
    [Fact]
    public void ARockCarryingARockRefusedInAnEarlierPassGoesBack()
    {
        var aNow = PlaceNow + new Vector3(0, 0, 2 * RockHalfSize);
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, aNow), Rock(1, new Vector3(-300, 0, 0), PlaceNow)],
            Others = [RockNow(0, aNow), RockNow(1, PlaceNow), crate],
        };

        Assert.Equal(
            [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), null],
            Plan(scene).Objects.Select(judged => judged.Refused));
    }

    /// <summary>
    /// A base game lantern stands on top of the rock where the cleaned mod put it. The rock goes back, and the follow-up removes the
    /// lantern, started by the rock, as nothing lies below it in this scene without ground. When a later mod's crate near the rock's
    /// old place refuses the rock, the rock stays and the follow-up of the last pass, which the patch writes, takes nothing away.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATrustedLanternOnARockGoingBackIsTakenAwayOnlyWhenTheRockGoesBack(bool crateRefusesTheRock)
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, 0));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow), .. crateRefusesTheRock ? [crate] : Array.Empty<OtherObject>()] };

        var result = Plan(scene);

        Assert.Equal(!crateRefusesTheRock, Assert.Single(result.Objects).IsRestored);
        Assert.Equal(
            crateRefusesTheRock ? [] : [(lantern.Key, OtherModsObjectFate.RemovedNothingBelow, RockKey(0).ToString())],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
        Assert.Empty(result.FollowUp.Targets);
    }

    /// <summary>
    /// A base game lantern stands on top of the rock where the cleaned mod put it, far from the rock's old place, and a block of the
    /// cleaned mod that stays lies 60 units below the rock, beyond the zones of every setting. The rock goes back and the follow-up pulls the lantern down onto the
    /// block. When the block's collision group judges it, the lantern where it lands is too close to it, so the rock is refused for the
    /// block and stays, and the lantern with it; otherwise the rock goes back and the lantern is pulled down. The same when the removal
    /// steps read the lantern as support only: pulled down, it stands where no trusted version puts it, so it is a cause where it lands.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ARockWhoseFollowUpPullsAnObjectTooCloseToWhatStaysStays(bool blockJudged, bool lanternSupportOnly)
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, FarPlaceNow + new Vector3(0, 0, RockHalfSize));
        var block = Own(1, FarPlaceNow + new Vector3(0, 0, -100));
        var scene = new Scene
        {
            Targets = [RemovedHouse(), block],
            Rocks = [Rock(0, OldPlace, FarPlaceNow)],
            Others = [.. lanternSupportOnly ? Array.Empty<OtherObject>() : [lantern], RockNow(0, FarPlaceNow)],
            SupportOnly = lanternSupportOnly ? [lantern] : [],
            StandingTooClose = blockJudged ? Settings.TooClose : null,
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(blockJudged ? (false, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, block.Key, Failure: null)) : (true, null), (rock.IsRestored, rock.Refused));
        Assert.Equal(
            blockJudged ? [] : [(lantern.Key, OtherModsObjectFate.PulledDown, RockKey(0).ToString())],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
    }

    /// <summary>
    /// A base game lantern no plugin changed stands on the first rock where the cleaned mod put it, above a base game slab. The first
    /// rock goes back and the follow-up pulls the lantern down onto the slab, 5 units beside the second rock's old place. The lantern
    /// stands as it stood before the cleaned mod, so the removal steps never take it as a cause, and the second rock still goes back.
    /// </summary>
    [Fact]
    public void AnObjectOnlySupportForTheRemovalStepsPulledBesideACandidateDoesNotKeepIt()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var slab = TestShapes.Placed(BaseGameMod, 6, Block.Base, PlaceNow + new Vector3(0, 0, -100));
        var secondOldPlace = PlaceNow + new Vector3(3 + 5 + RockHalfSize, 0, -80 + RockHalfSize);
        var secondNow = new Vector3(-300, 500, 0);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, secondOldPlace, secondNow)],
            Others = [lantern, RockNow(0, PlaceNow), RockNow(1, secondNow), slab],
        };

        var result = Plan(scene);

        Assert.All(result.Objects, judged => Assert.Equal((true, null), (judged.IsRestored, judged.Refused)));
        var pulled = Assert.Single(result.FollowUp.Others).Outcome;
        Assert.Equal((lantern.Key, OtherModsObjectFate.PulledDown), (pulled.Object.Key, pulled.Fate));
        Assert.Equal(-80f, pulled.NewPosition.Z, tolerance: 0.5f);
    }

    /// <summary>What stands where a candidate pulled down where it goes back would land.</summary>
    public enum AtTheLanding { Nothing, ChangedNavmesh, LaterModsLantern, CleanedModsSlabThatStays }

    /// <summary>
    /// A lantern B stood on rock A before the cleaned mod moved both away. A's old place lies in a navmesh the cleaned mod added, so A
    /// stays, and B would float where it goes back: the follow-up pulls it down onto a slab far below. B stays where the cleaned mod put
    /// it when the place it lands on fails a guard, and the reason names what is there: the navmesh, when it reaches down to where B
    /// lands; a later mod's lantern 5 units beside B there, beyond the reach of every old place; the slab, when it is an object of the
    /// cleaned mod that stays: the check of the slab by its own collision group, as the next patcher makes it, finds B on it and refuses
    /// B itself (109 more of the cleaned mod's objects around are removed, so the areas are cleared). With nothing there, B goes back and
    /// the pull-down is written.
    /// </summary>
    [Theory]
    [InlineData(AtTheLanding.Nothing)]
    [InlineData(AtTheLanding.ChangedNavmesh)]
    [InlineData(AtTheLanding.LaterModsLantern)]
    [InlineData(AtTheLanding.CleanedModsSlabThatStays)]
    public void ACandidatePulledDownWhereItGoesBackMustFitWhereItLands(AtTheLanding there)
    {
        var bNow = new Vector3(-300, 0, 0);
        var slabPlace = OldPlace + new Vector3(0, 0, -300);
        var landing = slabPlace + new Vector3(0, 0, RockHalfSize);
        var lantern = LaterObject(4, Lantern.Base, landing + new Vector3(3 + 5 + 3, 0, 0));
        var ownSlab = there == AtTheLanding.CleanedModsSlabThatStays;
        const int RemovedAround = 110;
        var slab = Own(RemovedAround, slabPlace);
        var scene = new Scene
        {
            Targets = [RemovedHouse(), .. Enumerable.Range(1, RemovedAround - 1).Select(index => Own(index, RemovedHouse().Transform.Position)), .. ownSlab ? [slab] : Array.Empty<TargetObject>()],
            Removed = [.. Enumerable.Range(0, RemovedAround)],
            StandingTooClose = Settings.TooClose,
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, OldPlace + new Vector3(0, 0, RockHalfSize), bNow, Lantern.Base)],
            Others =
            [
                RockNow(0, PlaceNow), RockNow(1, bNow, Lantern.Base),
                .. ownSlab ? Array.Empty<OtherObject>() : [TestShapes.Placed(BaseGameMod, 3, Block.Base, slabPlace)],
                .. there == AtTheLanding.LaterModsLantern ? [lantern] : Array.Empty<OtherObject>(),
            ],
            Navmesh =
            [
                new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(OldPlace)),
                .. there == AtTheLanding.ChangedNavmesh ? [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(landing + new Vector3(0, 0, 3)))] : Array.Empty<NavmeshWorldTriangle>(),
            ],
            ChangedNavmeshes = [ChangedNavmeshKey],
        };

        var result = Plan(scene);

        Assert.Equal(PlacedGuard.StandsInChangedNavmesh, result.Objects[0].Refused?.Guard);
        PlacedRefusal? refused = there switch
        {
            AtTheLanding.ChangedNavmesh => new PlacedRefusal(PlacedGuard.WouldNotFitWherePulledDown, ChangedNavmeshKey, Failure: null),
            AtTheLanding.LaterModsLantern => new PlacedRefusal(PlacedGuard.WouldNotFitWherePulledDown, lantern.Key, Failure: null),
            AtTheLanding.CleanedModsSlabThatStays => new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, slab.Key, Failure: null),
            _ => null,
        };
        Assert.Equal((refused is null, refused), (result.Objects[1].IsRestored, result.Objects[1].Refused));
        Assert.Equal(
            refused is null ? [(RockKey(1), OtherModsObjectFate.PulledDown, RockKey(1).ToString())] : [],
            result.FollowUp.Others.Select(other => (other.Outcome.Object.Key, other.Outcome.Fate, string.Join(", ", other.StartedBy))));
    }

    /// <summary>
    /// A pebble the cleaned mod moved onto the rock goes with the rock, and its removal restores it to where it stood before. When a
    /// later mod's lantern stands 5 units beside that place, far beyond the rock's own reach, the pebble would not fit there, so the
    /// rock stays and takes nothing along; otherwise the rock goes back and the pebble is restored.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockWhoseFollowUpRestoresAnObjectThatWouldNotFitStays(bool lanternThere)
    {
        var pebbleBefore = new Vector3(-300, -300, 0);
        var pebble = PebbleAt(1, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var scene = new Scene
        {
            Targets = [RemovedHouse(), pebble],
            Unprotected = [1],
            RestoredTo = new Dictionary<int, Vector3> { [1] = pebbleBefore },
            EarlierBases = new Dictionary<int, BaseKey> { [1] = Pebble.Base },
            Rocks = [Rock(0, OldPlace, PlaceNow)],
            Others = [RockNow(0, PlaceNow), .. lanternThere ? [LaterObject(2, Lantern.Base, pebbleBefore + new Vector3(1.5f + 5, 0, 0))] : Array.Empty<OtherObject>()],
        };

        var result = Plan(scene);

        var rock = Assert.Single(result.Objects);
        Assert.Equal(lanternThere ? (false, new PlacedRefusal(PlacedGuard.TakesAlongObjectThatWouldNotFit, pebble.Key, Failure: null)) : (true, null), (rock.IsRestored, rock.Refused));
        Assert.Equal(lanternThere ? [] : [pebble.Id], result.FollowUp.Targets.Select(followed => followed.Target));
    }

    /// <summary>
    /// Twenty rocks go back, each with a base game lantern on it. The follow-up refuses nothing, so the loop ends after its first pass,
    /// and the follow-up of that pass takes every lantern along, each started by its own rock.
    /// </summary>
    [Fact]
    public void TwentyRocksCarryingLanternsGoBackInOnePass()
    {
        const int Count = 20;
        List<Vector3> places = [.. Enumerable.Range(0, Count).Select(index => PlaceNow + new Vector3(0, 200 * index, 0))];
        var scene = new Scene
        {
            Rocks = [.. places.Select((now, index) => Rock(index, OldPlace + new Vector3(45 * (index % 5), -45 * (index / 5), 0), now))],
            Others =
            [
                .. places.Select((now, index) => RockNow(index, now)),
                .. places.Select((now, index) => TestShapes.Placed(BaseGameMod, 100 + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize))),
            ],
        };

        var result = Plan(scene);

        Assert.Equal(1, result.Passes);
        Assert.All(result.Objects, judged => Assert.True(judged.IsRestored));
        Assert.Equal(
            Enumerable.Range(0, Count).Select(index => RockKey(index).ToString()),
            result.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)));
    }

    /// <summary>
    /// A later mod's lantern stands on top of the rock where the cleaned mod put it, or a little above it. The lantern is no object of
    /// the cleaned mod, so the rock goes back from under it (decisions 31 and 32).
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void ARockGoesBackFromUnderALaterModsLantern(float gapBelowLantern)
    {
        var lantern = LaterObject(0, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize + gapBelowLantern));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A base game lantern no plugin changed stands on top of the rock where the cleaned mod put it; before the cleaned mod it stood
    /// there without the rock. The rock goes back to its earlier shape and place, so both end as they stood then.
    /// </summary>
    [Fact]
    public void ARockGoingBackToItsEarlierPlaceFromUnderAnObjectThatStoodThereBeforeTheCleanedModIsNotRefused()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A later mod that loads without the cleaned mod added a lantern on top of the rock where the cleaned mod put it, or moved a base
    /// game lantern there. The lantern is no object of the cleaned mod, so the rock goes back from under it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoesBackFromUnderAnObjectALaterModAddedOrMovedOntoIt(bool added)
    {
        var onTop = PlaceNow + new Vector3(0, 0, RockHalfSize);
        var lantern = added
            ? TestShapes.Placed(LaterMod, 5, Lantern.Base, onTop)
            : TestShapes.Placed(BaseGameMod, 5, Lantern.Base, onTop) with { WinningMod = Later };
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A later mod re-saved a base game lantern on top of the rock where the cleaned mod put it, with the same values: it stands as it
    /// stood before the cleaned mod, so the rock goes back from under it.
    /// </summary>
    [Fact]
    public void ARockGoingBackFromUnderAnObjectALaterModOnlyReSavedIsNotRefused()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize)) with { WinningMod = Later };
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
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

    /// <summary>The top of a rock at its old place, where a lantern stood on it before the cleaned mod moved both away.</summary>
    private static readonly Vector3 OnTheOldPlace = OldPlace + new Vector3(0, 0, RockHalfSize);

    private static readonly Vector3 LanternNow = new(-300, 0, 0);

    /// <summary>
    /// The cleaned mod moved a base game table and the lantern on it away from their old places. A later mod's crate, low beside the
    /// table's old place, refuses the table, which stays where the cleaned mod put it; the lantern still goes back, as its earlier
    /// version. Without the crate both go back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALanternGoesBackWhetherOrNotTheTableItStoodOnGoesBack(bool tableRefused)
    {
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, -RockHalfSize));
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, OnTheOldPlace, LanternNow, Lantern.Base)],
            Others = [RockNow(0, PlaceNow), RockNow(1, LanternNow, Lantern.Base), .. tableRefused ? new[] { crate } : []],
        };

        var refusals = Plan(scene).Objects.Select(judged => judged.Refused);

        Assert.Equal(
            tableRefused
                ? [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), null]
                : new PlacedRefusal?[] { null, null },
            refusals);
    }

    /// <summary>The cleaned mod moved a lantern off a base game table; the patch removes the table, or keeps it. The lantern goes back either way.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALanternGoesBackWhetherOrNotThePatchRemovesTheTableItStoodOn(bool tableRemoved)
    {
        var table = TestShapes.Placed(BaseGameMod, 1, Block.Base, OldPlace) with { Id = new OtherId(0) };
        var removal = new OtherModsObjectOutcome(table, OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None);
        var scene = new Scene
        {
            Rocks = [Rock(0, OnTheOldPlace, LanternNow, Lantern.Base)],
            Others = [table, RockNow(0, LanternNow, Lantern.Base)],
            OtherMods = tableRemoved ? new OtherModsObjectsResult(StepOutcome.Performed, [removal], [], [], Failure: null) : OtherModsObjectsResult.SwitchedOff,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    [Fact]
    public void TheGuardsGiveTheSameResultsWhateverTheNumberOfWorkers()
    {
        var one = Plan(ManyRocks(workers: 1));
        var eight = Plan(ManyRocks(workers: 8));

        Assert.Equal(one.Objects.Select(Describe), eight.Objects.Select(Describe));
        Assert.Equal(one.FollowUp.Others.Select(other => other.Outcome), eight.FollowUp.Others.Select(other => other.Outcome));
        Assert.Equal(one.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)), eight.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)));
        Assert.Contains(one.Objects, judged => judged.IsRestored);
        Assert.Equal(
            [PlacedGuard.TooCloseToWhatStays, PlacedGuard.StandsInChangedNavmesh],
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
    /// marker of the cleaned mod, into a navmesh the cleaned mod added, or carry a later mod's lantern or a base game lantern no plugin
    /// changed where they stand, and some were moved onto the next rock's old place.
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
            if (index % 8 == 5) others.Add(TestShapes.Placed(BaseGameMod, 2 * Count + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize)));
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

        /// <summary>The targets that are not removed and that no quest protects; every other one that is not removed is protected.</summary>
        public int[] Unprotected { get; init; } = [];

        /// <summary>The removed targets that go back to where they stood before the cleaned mod instead of being disabled, with that place.</summary>
        public Dictionary<int, Vector3> RestoredTo { get; init; } = [];

        /// <summary>The base a restored target had before the cleaned mod replaced it; the target's own base when not listed.</summary>
        public Dictionary<int, BaseKey> EarlierBases { get; init; } = [];

        public List<RecordChangedByCleanedMod> Rocks { get; init; } = [];

        /// <summary>The other plugins' objects that stand now, numbered in this order.</summary>
        public List<OtherObject> Others { get; init; } = [];

        /// <summary>The objects that stand now that the removal steps read as support only, numbered after <see cref="Others"/>.</summary>
        public List<OtherObject> SupportOnly { get; init; } = [];

        public List<NavmeshWorldTriangle> Navmesh { get; init; } = [];
        public List<RecordKey> ChangedNavmeshes { get; init; } = [];

        /// <summary>What became of other mods' objects left without support; their ids are their places in <see cref="Others"/>.</summary>
        public OtherModsObjectsResult OtherMods { get; init; } = OtherModsObjectsResult.SwitchedOff;

        /// <summary>The too-close settings of the collision group that judges the targets that are not removed; none judges them when null.</summary>
        public TooCloseSettings? StandingTooClose { get; init; }

        /// <summary>How far the removal steps believe Later.esp.</summary>
        public VersionTrust LaterTrust { get; init; } = VersionTrust.None;

        /// <summary>The removal steps' follow-up settings.</summary>
        public AlsoRemoveSettings FollowUp { get; init; } = TestPlacedGuardRules.DefaultFollowUp;

        /// <summary>The zone shape of the restore's too-close settings.</summary>
        public ZoneShape Zone { get; init; } = Settings.TooClose.Zone;

        public int Workers { get; init; } = 1;
    }

    /// <summary>The cleaned mod's only visible object near the old places, which the rounds removed.</summary>
    private static TargetObject RemovedHouse() => Own(0, new Vector3(200, 300, 0));

    /// <summary>The cleaned mod's second object: a base game crate it moved, which a removal step puts back.</summary>
    private static TargetObject MovedCrate() => Own(1, new Vector3(-300, 300, 0)) with { Origin = TargetOrigin.Moved };

    /// <summary>
    /// The house and <see cref="MovedCrate"/> are removed, and the crate goes back to <paramref name="distance"/> east of the rock's old
    /// place, where the rock goes back to; the rock stands at <see cref="PlaceNow"/> meanwhile.
    /// </summary>
    private static Scene RestoredCrateBesideRock(RecordChangedByCleanedMod rock, float distance = ZoneReach - 5f) =>
        new()
        {
            Targets = [RemovedHouse(), MovedCrate()],
            Removed = [0, 1],
            RestoredTo = new Dictionary<int, Vector3> { [1] = OldPlace + new Vector3(distance, 0, 0) },
            Rocks = [rock],
            Others = [RockNow(0, PlaceNow)],
        };

    private static TargetObject Own(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), Block.Base, TestTargets.Space);

    private static TargetObject PebbleAt(int index, Vector3 position) => TestTargets.Create(index, TestTargets.At(position), Pebble.Base, TestTargets.Space);

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
        List<OtherObject> others = [.. scene.Others.Select((other, index) => other with { Id = new OtherId(index) })];
        List<OtherObject> supportOnly = [.. scene.SupportOnly.Select((other, index) => other with { Id = new OtherId(others.Count + index), Change = null })];
        var collected = TestScenes.CreateWorld(targets, others, supportOnly) with
        {
            ChangedRecords = [.. scene.Rocks],
            ChangedNavmeshes = [.. scene.ChangedNavmeshes.Select(ChangedNavmesh)],
        };
        var rules = TestPlacedGuardRules.Create(Shapes, Trust, Plugins, Support, TestGround.NoTerrain(), scene.Workers);
        ImmutableArray<NavmeshWorldTriangle> navmesh = [.. scene.Navmesh];
        var decisions = DecisionsRemoving(targets, scene.Removed, scene.RestoredTo, scene.EarlierBases, scene.Unprotected);
        var caches = new TestRunCaches(
            Shapes,
            terrain: TestGround.NoTerrain(),
            navmeshes: new NavmeshIndex(bucket => bucket.Grid is null && !navmesh.IsEmpty ? [.. navmesh] : null),
            shownTargets: TestScenes.Create(targets, [], Shapes).IndexShownTargets);
        return WhereRemovedRestorer.Plan(
            new(
                new(collected, scene.OtherMods),
                decisions,
                TestClearedAreas.Of(collected, decisions, caches, Settings),
                rules.Over(collected, alsoRemove: scene.FollowUp),
                new(
                    TestTargetGroups.None.ChangedTerrain,
                    scene.StandingTooClose is { } standingTooClose
                        ? TestTargetGroups.SettingsOf(
                            Enumerable.Range(0, targets.Length).Except(scene.Removed).Select(index => new TargetId(index)), standingTooClose)
                        : TestTargetGroups.None.TooClose,
                    new AcceptedWorld(
                        TestTargets.PatchName,
                        ImmutableDictionary<PluginName, VersionTrust>.Empty.Add(BaseGame, VersionTrust.Full).Add(TestTargets.TargetName, VersionTrust.Full).Add(Later, scene.LaterTrust)),
                    LeftBehindNeighbours: [],
                    TestTargetGroups.None.CleanedModsObjects,
                    TestTerrainJudging.FindsNothing),
                new(
                    caches,
                    TestLandPlugin.Flat,
                    Plugins,
                    TestTargets.TargetName),
                new Execution(scene.Workers),
                Settings with { TooClose = Settings.TooClose with { Zone = scene.Zone } },
                () => TimeSpan.Zero));
    }

    /// <param name="restoredTo">The removed targets that go back to the given place instead of being disabled.</param>
    /// <remarks>Every target that is not removed is an object to keep (a record of the cleaned mod links to it).</remarks>
    /// <param name="unprotected">The targets that are not removed and that a quest does not protect.</param>
    private static RemovalDecisions DecisionsRemoving(
        ImmutableArray<TargetObject> targets, int[] removed, IReadOnlyDictionary<int, Vector3> restoredTo, IReadOnlyDictionary<int, BaseKey> earlierBases, int[]? unprotected = null) =>
        RemovalDecisions.Start(
                ObjectsToKeep.Build(
                    targets,
                    [],
                    TestTargets.References(targets.Length, Enumerable.Range(0, targets.Length).Except(removed).Except(unprotected ?? []).ToDictionary(index => index, _ => TestKeepReasons.Quest)),
                    TestBaseFacts.Unresolved),
                [.. targets.Select((target, index) => WhenRemovedOf(restoredTo.TryGetValue(index, out var place) ? place : (Vector3?)null, earlierBases.TryGetValue(index, out var earlierBase) ? earlierBase : target.Base))])
            .Apply(RoundKind.TooClose, [.. removed.Select(index => new ProposedRemoval(new TargetId(index), new TooClose(new OtherId(0), new TooCloseCause.ObjectSize())))]);

    private static WhenRemoved WhenRemovedOf(Vector3? restoredTo, BaseKey? earlierBase) =>
        restoredTo is { } place
            ? new WhenRemoved.Restore(TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(place, Vector3.Zero, 1f), earlierBase))
            : new WhenRemoved.Disable();
}
