using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedGuardScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The too-close guard of the placed records that go back where the cleaned mod was removed, against other mods' objects the patch
/// pulls down or removes and against objects a removal step put back, over in-memory scenes.
/// </summary>
public sealed class RestoreWhereRemovedGuardRestoredObjectsTests
{
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
}
