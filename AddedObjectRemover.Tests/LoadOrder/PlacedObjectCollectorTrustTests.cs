using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// Another mod's record is judged once by the accepted-value rule over its versions: changed, it is an other-mod object that names
/// the changing mod and where the accepted value came from; unchanged, it is support only. Skyrim.esm and Carrier.esp are trusted,
/// Birds.esp is trusted for its own changes, CityMod.esp and Later.esp are untrusted; the patch is transparent.
/// </summary>
public class PlacedObjectCollectorTrustTests
{
    public enum Property { Position, Rotation, Scale, Base, Presence }

    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName Carrier = new("Carrier.esp");
    private static readonly PluginName Birds = new("Birds.esp");
    private static readonly PluginName City = new("CityMod.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Patch = TestTargets.PatchName;

    private static readonly Vector3 Placed = new(500, 0, 0);
    private static readonly Vector3 Moved = new(600, 0, 0);
    private static readonly Vector3 MovedAgain = new(700, 0, 0);
    private static readonly BaseKey Wall = new(new RecordKey(BaseGame, 0x100), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey OtherWall = new(new RecordKey(BaseGame, 0x101), BaseLinkKind.PlaceableObject);
    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);

    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(
        TestTargets.TargetName, Patch, trusted: [BaseGame, Carrier], untrusted: [City, Later], trustedForOwnChanges: [Birds]);

    private static readonly PlacedRecordFact Target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);

    [Fact]
    public void AnObjectATrustedModPlacedAndNoOneChangedIsSupportOnly()
    {
        var collected = Collect(Record(BaseGame, At(BaseGame, Placed)));

        Assert.Empty(collected.OtherModObjects);
        Assert.Single(collected.SupportOnlyObjects);
        Assert.Equal(1, collected.Counts.OthersUnchanged);
    }

    [Fact]
    public void AnUnchangedObjectWithAPositionThatIsNotANumberCountsAsAnInvalidPlacement()
    {
        var collected = Collect(Record(BaseGame, At(BaseGame, new Vector3(float.NaN, 0, 0))));

        Assert.Equal((0, 1), (collected.Counts.OthersUnchanged, collected.Counts.OtherInvalidPlacements));
    }

    [Fact]
    public void AnObjectAnUntrustedModAddedIsANewObject() =>
        Assert.Equal(new ObjectChange(City, AcceptedFrom: null), ChangeOf(Record(City, At(City, Placed))));

    [Theory]
    [InlineData(Property.Position)]
    [InlineData(Property.Rotation)]
    [InlineData(Property.Scale)]
    [InlineData(Property.Base)]
    [InlineData(Property.Presence)]
    public void AnUntrustedModChangingAnyObjectPropertyOfATrustedModsObjectChangesIt(Property property) =>
        Assert.Equal(new ObjectChange(City, BaseGame), ChangeOf(Record(BaseGame, At(BaseGame, Placed), Changed(At(City, Placed), property))));

    /// <summary>Position, rotation and scale each moved by less than their tolerance, as a plugin re-saving the record rounds them.</summary>
    [Fact]
    public void AnUntrustedReSaveWithinTheTolerancesChangesNothing()
    {
        var resaved = new PlacedVersionFact(
            City, new PlacementFacts(Shown, Placed + new Vector3(0.005f, 0, 0), new Vector3(0, 0, 0.00005f)), Scale: 1.00005f, Wall);

        Assert.Null(ChangeOf(Record(BaseGame, At(BaseGame, Placed), resaved)));
    }

    /// <summary>The same facing stored a full turn apart on each axis is no rotation change.</summary>
    [Fact]
    public void AnUntrustedReSaveOfTheSameFacingAFullTurnApartChangesNothing()
    {
        var original = At(BaseGame, Placed) with { Placement = new PlacementFacts(Shown, Placed, new Vector3(0.5f, -3f, -0.00005f)) };
        var resaved = At(City, Placed) with { Placement = new PlacementFacts(Shown, Placed, new Vector3(0.5f - MathF.Tau, MathF.Tau - 3f, 6.28313f)) };

        Assert.Null(ChangeOf(Record(BaseGame, original, resaved)));
    }

    [Fact]
    public void ARotationPastTheToleranceAcrossAFullTurnStillChangesTheObject()
    {
        var original = At(BaseGame, Placed) with { Placement = new PlacementFacts(Shown, Placed, new Vector3(0, 0, 0.1f)) };
        var turned = At(City, Placed) with { Placement = new PlacementFacts(Shown, Placed, new Vector3(0, 0, MathF.Tau - 0.1f)) };

        Assert.Equal(new ObjectChange(City, BaseGame), ChangeOf(Record(BaseGame, original, turned)));
    }

    [Fact]
    public void TheChangedObjectStandsWhereTheGameShowsIt()
    {
        var other = Assert.Single(Collect(Record(BaseGame, At(BaseGame, Placed), At(City, Moved))).OtherModObjects);

        Assert.Equal(Moved, other.Position);
        Assert.Equal(City, other.WinningMod);
    }

    [Fact]
    public void AnUntrustedPluginChangingOnlyOtherDataHandsNothingOver() =>
        Assert.Null(ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(Later, Placed))));

    [Fact]
    public void AnUntrustedPluginCarryingAnotherUntrustedModsMoveLeavesThatModNamed() =>
        Assert.Equal(new ObjectChange(City, BaseGame), ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Later, Moved))));

    [Fact]
    public void ATrustedPluginCarryingAnUntrustedModsValueMakesItAccepted() =>
        Assert.Null(ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Carrier, Moved))));

    [Fact]
    public void TheHighestTrustedVersionHoldsTheAcceptedValue() =>
        Assert.Equal(new ObjectChange(City, Carrier), ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(Carrier, Moved), At(City, MovedAgain))));

    [Fact]
    public void AnObjectAModTrustedForItsOwnChangesAddsIsAccepted() =>
        Assert.Null(ChangeOf(Record(Birds, At(Birds, Placed))));

    [Fact]
    public void AnUntrustedModMovingAnObjectAModTrustedForItsOwnChangesAddedNamesThatMod() =>
        Assert.Equal(new ObjectChange(City, Birds), ChangeOf(Record(Birds, At(Birds, Placed), At(City, Moved))));

    [Fact]
    public void AModTrustedForItsOwnChangesMovingAnObjectAgainMakesItsMoveAccepted() =>
        Assert.Null(ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Birds, MovedAgain))));

    /// <summary>Birds.esp owns only the scale it changes; the position it carries stays City's change, so the winner is not named.</summary>
    [Fact]
    public void AModTrustedForItsOwnChangesCarryingAnUntrustedMoveLeavesTheMoveChanged() =>
        Assert.Equal(
            new ObjectChange(City, BaseGame),
            ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Birds, Moved) with { Scale = 2f })));

    /// <summary>An earlier patcher of the group copied City's move into the patch: the patch is skipped and City is named.</summary>
    [Fact]
    public void ThePatchCarryingAnUntrustedModsMoveNamesThatMod() =>
        Assert.Equal(new ObjectChange(City, BaseGame), ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Patch, Moved))));

    [Fact]
    public void ThePatchCarryingATrustedValueChangesNothing() =>
        Assert.Null(ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(Patch, Placed))));

    [Fact]
    public void ACopyInThePatchIsNeverTheAcceptedValue() =>
        Assert.Equal(new ObjectChange(City, BaseGame), ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(Patch, Moved), At(City, Moved))));

    [Fact]
    public void AMoveOnlyThePatchHoldsNamesNoPlugin() =>
        Assert.Equal(new ObjectChange(ChangingMod: null, BaseGame), ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(Patch, Moved))));

    /// <summary>
    /// City swaps the base and an earlier patcher moves the object in the patch: the patch's change loads last, but the real mod's
    /// change is named, with the accepted value of its own property.
    /// </summary>
    [Fact]
    public void ARealModsChangeIsNamedBeforeAChangeOnlyThePatchHolds() =>
        Assert.Equal(
            new ObjectChange(City, BaseGame),
            ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Placed) with { Base = OtherWall }, At(Patch, Moved) with { Base = OtherWall })));

    [Fact]
    public void AnObjectAnEarlierPatcherAddedToThePatchIsANewObject() =>
        Assert.Equal(new ObjectChange(ChangingMod: null, AcceptedFrom: null), ChangeOf(Record(Patch, At(Patch, Placed))));

    /// <summary>City moves the object and Later scales it: Later's change loads last, and the scale was accepted from the base game.</summary>
    [Fact]
    public void OfSeveralChangedPropertiesTheLaterLoadingChangingModIsNamed() =>
        Assert.Equal(
            new ObjectChange(Later, BaseGame),
            ChangeOf(Record(BaseGame, At(BaseGame, Placed), At(City, Moved), At(Later, Moved) with { Scale = 2f })));

    /// <summary>
    /// Birds.esp scales the base game's object, City moves it and Later scales it again: the scale, which Later changed last, was
    /// accepted from Birds.esp; the position was accepted from the base game.
    /// </summary>
    [Fact]
    public void TheNamedChangeTakesTheAcceptedValueOfItsOwnProperty() =>
        Assert.Equal(
            new ObjectChange(Later, Birds),
            ChangeOf(Record(
                BaseGame,
                At(BaseGame, Placed),
                At(Birds, Placed) with { Scale = 2f },
                At(City, Moved) with { Scale = 2f },
                At(Later, Moved) with { Scale = 3f })));

    [Fact]
    public void AnUnchangedObjectOutsideTheTargetSpacesIsNotCounted()
    {
        var hidden = Record(BaseGame, At(BaseGame, Placed)) with { InTargetSpace = false };

        Assert.Equal(0, Collect(hidden).Counts.OthersUnchanged);
    }

    [Fact]
    public void TheScanStopsOnlyAtAFullyTrustedPlugin()
    {
        var plugin = new FixedFactsPlugin([Target]);

        PlacedObjectCollector.Collect(plugin, Mods, Plan);

        var stops = plugin.StopsTheWalk!;
        Assert.Equal((true, true, false, false, false), (stops(BaseGame), stops(Carrier), stops(Birds), stops(City), stops(Patch)));
    }

    private static PlacedVersionFact At(PluginName plugin, Vector3 position) =>
        new(plugin, new PlacementFacts(Shown, position, Vector3.Zero), Scale: null, Wall);

    private static PlacedVersionFact Changed(PlacedVersionFact version, Property property) => property switch
    {
        Property.Position => version with { Placement = version.Placement with { Position = Moved } },
        Property.Rotation => version with { Placement = version.Placement with { EulerRotation = new Vector3(0, 0, 1f) } },
        Property.Scale => version with { Scale = 2f },
        Property.Base => version with { Base = OtherWall },
        Property.Presence => version with
        {
            Placement = version.Placement with { Presence = Shown with { InitiallyDisabled = true, EnableParent = new EnableParentFact(TestTargets.Key(9), Opposite: false) } },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };

    /// <param name="versions">Lowest first, the winner last.</param>
    private static PlacedRecordFact Record(PluginName creator, params PlacedVersionFact[] versions)
    {
        var winner = versions[^1];
        return TestPlacedRecords.Create(new RecordKey(creator, 1), winner.Plugin, winner.Placement.Position!.Value, winner.Base) with { Versions = [.. versions] };
    }

    /// <returns>Null when the record is support only.</returns>
    private static ObjectChange? ChangeOf(PlacedRecordFact record)
    {
        var collected = Collect(record);
        return collected.OtherModObjects.IsEmpty ? null : Assert.Single(collected.OtherModObjects).Change;
    }

    private static CollectedObjects Collect(PlacedRecordFact record) =>
        PlacedObjectCollector.Collect(new FixedFactsPlugin(ImmutableArray.Create(Target, record)), Mods, Plan);
}
