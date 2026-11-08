using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedGuardScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The too-close and changed-navmesh guards of the placed records that go back where the cleaned mod was removed, over in-memory
/// scenes: base game rocks the cleaned mod moved away from a clearing, each checked at the place it goes back to.
/// </summary>
public sealed class RestoreWhereRemovedGuardTests
{
    /// <summary>A collision layer number far beyond the layers the game defines, so no collision layer record carries it.</summary>
    private const uint NumberNoLayerHas = 9999;

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
}
