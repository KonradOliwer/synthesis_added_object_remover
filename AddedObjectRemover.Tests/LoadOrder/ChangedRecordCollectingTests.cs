using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// Which parts of another plugin's placed record go back to the version below the cleaned mod's, read from in-memory plugins by the
/// real reader and collector: the base game places a rock, the cleaned mod overrides it, and a later plugin and the patch may
/// override it again.
/// </summary>
public sealed class ChangedRecordCollectingTests
{
    private static readonly Vector3 Placed = new(100, 200, 0);
    private static readonly Vector3 Moved = new(900, 200, 0);
    private static readonly Vector3 MovedAgain = new(1500, 200, 0);
    private const ushort OlderFormVersion = 43;
    private const ushort NewerFormVersion = 44;
    private const int InitiallyDisabled = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

    private static readonly WhatToCollect Plan = new(new ScanNeeds(AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(LandHeight: 8f, WaterHeight: 8f), Cells: true, Lands: true)), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);
    private static readonly PluginName Patch = FixturePatcherState.PatchModKey.ToPluginName();

    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _cleaned;
    private readonly FixtureMod _later;
    private readonly FixtureMod _below;
    private readonly Worldspace _worldspace;
    private readonly Cell _cell;
    private readonly PlacedObject _rock;
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];
    private bool _belowLoadsBelowTheCleanedMod;

    public ChangedRecordCollectingTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _below = new FixtureMod("Below.esp", _base);
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        _worldspace = new Worldspace(_base.NextKey(), FixtureMod.Release);
        _worldspace.AddCell(_cell);
        _base.Mod.Worldspaces.Add(_worldspace);
        _rock = new PlacedObject(_base.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = ToP3Float(Placed) } };
        _rock.Base.SetTo(_base.AddMarkerStatic("RockBase"));
        _cell.Temporary.Add(_rock);
    }

    private RecordKey Rock => _rock.FormKey.ToRecordKey();

    /// <summary>A later plugin that needs the cleaned mod carries its move over: the move goes back to the version below the cleaned mod.</summary>
    [Fact]
    public void AMoveAPluginThatNeedsTheCleanedModCarriesOverGoesBackToTheVersionBelowTheCleanedMod()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var collected = Collect(Trust(laterTrusted: false, laterNeedsCleanedMod: true));

        var changed = ChangedRecordOf(collected);
        Assert.Equal(PlacedParts.Position, changed.Earlier.Undo);
        Assert.Empty(changed.Earlier.LeftToLaterPlugins);
        Assert.Equal((_base.Key.ToPluginName(), Placed), (changed.Earlier.Plugin, changed.Earlier.Placement.Position));
        Assert.Equal((_later.Key.ToPluginName(), (Vector3?)Moved), (changed.WinningMod, changed.WhereItIsNow?.Position));
        Assert.Equal(TargetOrigin.Moved, Assert.Single(collected.Targets, target => target.Key == Rock).Origin);
    }

    /// <summary>
    /// A patch of another group loads between the base game and the cleaned mod and holds a position of its own; this group's patch
    /// moves the rock again. Leaving the patches out for the second look must not shift the cleaned mod's version: the rock is still
    /// judged as moved by the cleaned mod, from the other group's patch's place.
    /// </summary>
    [Fact]
    public void AMoveIsStillJudgedAsMovedWhenAnotherGroupsPatchLoadsBelowTheCleanedMod()
    {
        _belowLoadsBelowTheCleanedMod = true;
        Override(_below.Mod, _rock).Placement!.Position = ToP3Float(new Vector3(300, 200, 0));
        var moved = MoveInCleanedMod();
        var trust = TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: [_base.Key.ToPluginName()], untrusted: [_later.Key.ToPluginName()], otherGroupPatches: [_below.Key.ToPluginName()]);

        var collected = Collect(trust, patch => Override(patch, moved).Placement!.Position = ToP3Float(MovedAgain));

        var target = Assert.Single(collected.Targets, target => target.Key == Rock);
        Assert.Equal((TargetOrigin.Moved, _below.Key.ToPluginName()), (target.Origin, target.EarlierVersion!.Plugin));
    }

    /// <summary>The winning plugin is named as the one that put the object where it stands, unless it is the cleaned mod or the patch.</summary>
    [Fact]
    public void TheWinnerWhosePlaceDiffersFromTheCleanedModsIsNamedAsTheOneThatPutTheObjectThere()
    {
        Override(_later.Mod, MoveInCleanedMod()).Placement!.Position = ToP3Float(MovedAgain);

        Assert.Equal(_later.Key.ToPluginName(), ChangedRecordOf(Collect(Trust(laterTrusted: false, laterNeedsCleanedMod: true))).PutThereBy);
    }

    /// <summary>No one is named when the winner past the patches holds the cleaned mod's place: a later plugin's copy, or another group's patch's copy, which is passed over.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoOneIsNamedWhenTheWinnerPastThePatchesOnlyHoldsTheCleanedModsPlace(bool laterIsAnotherGroupsPatch)
    {
        Override(_later.Mod, MoveInCleanedMod());

        var trust = Trust(laterTrusted: false, laterNeedsCleanedMod: !laterIsAnotherGroupsPatch, laterIsOtherGroupsPatch: laterIsAnotherGroupsPatch);

        var changed = ChangedRecordOf(Collect(trust));
        Assert.Null(changed.PutThereBy);
        Assert.Equal(laterIsAnotherGroupsPatch ? _cleaned.Key.ToPluginName() : _later.Key.ToPluginName(), changed.WinningModPastPatches);
    }

    /// <summary>
    /// A later plugin, or another group's patch, moved the rock again, and this group's patch copied that version: the patch is passed
    /// over, so the plugin that moved it is the winner past the patches and is named as the one that put the rock there.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePluginThatMovedItAgainBelowThePatchIsTheWinnerPastThePatchesAndIsNamed(bool laterIsAnotherGroupsPatch)
    {
        var movedAgain = Override(_later.Mod, MoveInCleanedMod());
        movedAgain.Placement!.Position = ToP3Float(MovedAgain);
        var trust = Trust(laterTrusted: false, laterNeedsCleanedMod: !laterIsAnotherGroupsPatch, laterIsOtherGroupsPatch: laterIsAnotherGroupsPatch);

        var changed = ChangedRecordOf(Collect(trust, patch => Override(patch, movedAgain)));

        var later = _later.Key.ToPluginName();
        Assert.Equal((Patch, later, later), (changed.WinningMod, changed.WinningModPastPatches, (PluginName?)changed.PutThereBy));
    }

    /// <summary>The patch of another group copied the cleaned mod's move: it carried it over, so the position goes back and the removal steps judge the rock as moved.</summary>
    [Fact]
    public void AMoveAnotherGroupsPatchCopiedGoesBackAndIsJudgedAsMoved()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var target = Assert.Single(Collect(Trust(laterTrusted: false, laterIsOtherGroupsPatch: true)).Targets, target => target.Key == Rock);

        Assert.Equal((TargetOrigin.Moved, PlacedParts.Position), (target.Origin, target.EarlierVersion!.Undo));
    }

    /// <summary>
    /// A later plugin that loads without the cleaned mod was made against the base game's version, so holding the cleaned mod's
    /// position it set that position itself: the position is left to it.
    /// </summary>
    [Fact]
    public void AMoveALaterPluginThatLoadsWithoutTheCleanedModHoldsIsLeftToThatPlugin()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.None, changed.Earlier.Undo);
        Assert.Equal([KeyValuePair.Create(PlacedParts.Position, _later.Key.ToPluginName())], changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>A later plugin that needs the cleaned mod moves the rock again: its move keeps nothing, so the position goes back.</summary>
    [Fact]
    public void AMoveAPluginThatNeedsTheCleanedModMakesAgainGoesBack()
    {
        Override(_later.Mod, MoveInCleanedMod()).Placement!.Position = ToP3Float(MovedAgain);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false, laterNeedsCleanedMod: true)));

        Assert.Equal((PlacedParts.Position, 0), (changed.Earlier.Undo, changed.Earlier.LeftToLaterPlugins.Count));
    }

    [Fact]
    public void AMoveALaterPluginMovesAgainIsLeftToThatPlugin()
    {
        Override(_later.Mod, MoveInCleanedMod()).Placement!.Position = ToP3Float(MovedAgain);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.None, changed.Earlier.Undo);
        Assert.Equal([KeyValuePair.Create(PlacedParts.Position, _later.Key.ToPluginName())], changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>
    /// The cleaned mod moves the rock and raises the cell's land; the old places are not judged. A trusted later plugin re-saves one
    /// base game lantern with the same values and moves another. Only the moved one is changed since before the cleaned mod, which the
    /// scan reads below the later plugin, and nothing is decided in the world without the cleaned mod. Without the raised land, no
    /// object is decided in the world before the cleaned mod either.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnObjectALaterPluginOnlyReSavesStandsAsBeforeTheCleanedModAndOneItMovesDoesNot(bool landRaised)
    {
        MoveInCleanedMod();
        if (landRaised) RaiseLandInCleanedMod();
        var resaved = PlaceLever();
        var moved = PlaceLever();
        Override(_later.Mod, resaved);
        Override(_later.Mod, moved).Placement!.Position = ToP3Float(MovedAgain);
        var plan = Plan with { Needs = Plan.Needs with { DecideEveryObjectBeforeCleanedMod = true } };
        var baseGame = _base.Key.ToPluginName();
        var trust = TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: [baseGame, _later.Key.ToPluginName()], untrusted: [], loadingBefore: [baseGame]);

        var supportOnly = Collect(trust, writeIntoPatch: null, records => records, plan).SupportOnlyObjects.ToDictionary(other => other.Key);

        Assert.Equal(
            ((ObjectChange?)null, landRaised ? new ObjectChange(_later.Key.ToPluginName(), baseGame) : null),
            (supportOnly[resaved.FormKey.ToRecordKey()].ChangeSinceBeforeCleanedMod, supportOnly[moved.FormKey.ToRecordKey()].ChangeSinceBeforeCleanedMod));
        Assert.All(supportOnly.Values, other => Assert.Null(other.ChangeWithoutCleanedMod));
    }

    /// <summary>
    /// An earlier run gave the patch a lever's position from a later plugin, which the patch still holds. The given-back value is
    /// accepted only in the world the run judges in: in the worlds without and before the cleaned mod it may be the cleaned mod's own,
    /// so there the lever still counts as changed. Untrusted and loading after the cleaned mod, the later plugin is believed in neither
    /// the accepted world nor the world before the cleaned mod; needing the cleaned mod, it loads in no world without it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AValueAnEarlierRunGaveBackCountsOnlyInTheWorldTheRunJudgesIn(bool laterNeedsCleanedMod)
    {
        MoveInCleanedMod();
        RaiseLandInCleanedMod();
        var lever = PlaceLever();
        var movedByLater = Override(_later.Mod, lever);
        movedByLater.Placement!.Position = ToP3Float(MovedAgain);
        var plan = Plan with { Needs = Plan.Needs with { JudgeOldPlaces = true, DecideEveryObjectWithoutCleanedMod = true, DecideEveryObjectBeforeCleanedMod = true } };
        var restored = new RestoredPart(new PluginName("EarlierCleaned.esp"), lever.FormKey.ToRecordKey(), new RestoredPartName.Placed(PlacedParts.Position), _later.Key.ToPluginName());

        var collected = Collect(Trust(laterTrusted: false, laterNeedsCleanedMod), patch => Override(patch, movedByLater), records => records, plan, [restored]);

        var given = Assert.Single(collected.SupportOnlyObjects, other => other.Key == lever.FormKey.ToRecordKey());
        Assert.DoesNotContain(collected.OtherModObjects, other => other.Key == given.Key);
        Assert.NotNull(given.ChangeSinceBeforeCleanedMod);
        Assert.Equal(laterNeedsCleanedMod, given.ChangeWithoutCleanedMod is not null);
    }

    /// <summary>
    /// The walk's stop rule is told whether the cleaned mod overrides a LAND record, so a run whose cleaned mod changed no ground
    /// walks no deeper for the world before the cleaned mod.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWalkIsToldWhetherTheCleanedModOverridesALandRecord(bool landRaised)
    {
        MoveInCleanedMod();
        if (landRaised) RaiseLandInCleanedMod();
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        using var world = FixturePatcherState.CreateFinalWorld(state);
        HashSet<bool> told = [];

        new PluginRecordsFactory(world, Environment.ProcessorCount).Open().ReadPlacedRecords(
            new PlacedReadScope(_cleaned.Key.ToPluginName(), Terrain: true, Navmeshes: false, TerrainVersions: false, TerrainLayers: false, Water: false, TestRecordKeys.HoldKeyword, TerrainReadsNeedAnOverride: false, ResavedPlacedParts.Tolerances, Plan.Needs.AreaRecordVersions),
            (_, _, overridesLand) =>
            {
                told.Add(overridesLand);
                return false;
            });

        Assert.Equal([landRaised], told);
    }

    /// <summary>The base game's flat land of the rock's cell, which the cleaned mod raises.</summary>
    private void RaiseLandInCleanedMod()
    {
        var land = _base.NextKey();
        _cell.Landscape = RestoreWhereRemovedWorlds.FlatLand(land, RestoreWhereRemovedWorlds.BaseLandSteps);
        CellCopyIn(_cleaned.Mod).Landscape = RestoreWhereRemovedWorlds.FlatLand(land, RestoreWhereRemovedWorlds.CleanedModLandSteps);
    }

    /// <summary>The cleaned mod moves and turns the rock; the later plugin moves it again, and the patch turns it again.</summary>
    [Fact]
    public void EachPartLeftToLaterPluginsNamesThePluginThatLastChangedThatPart()
    {
        var moved = MoveInCleanedMod();
        moved.Placement!.Rotation = new P3Float(0, 0, 1);
        var movedAgain = Override(_later.Mod, moved);
        movedAgain.Placement!.Position = ToP3Float(MovedAgain);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false), patch => Override(patch, movedAgain).Placement!.Rotation = new P3Float(0, 0, 2)));

        Assert.Equal(
            [KeyValuePair.Create(PlacedParts.Position, _later.Key.ToPluginName()), KeyValuePair.Create(PlacedParts.Rotation, Patch)],
            changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>
    /// The later plugin, a patch for the cleaned mod, carries the move over and disables the rock: a record removed after the cleaned
    /// mod gets nothing back.
    /// </summary>
    [Fact]
    public void NothingGoesBackInARecordALaterPluginDisables()
    {
        Override(_later.Mod, MoveInCleanedMod()).MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

        Assert.DoesNotContain(Collect(Trust(laterTrusted: false, laterNeedsCleanedMod: true)).ChangedRecords, changed => changed.Key == Rock);
    }

    /// <summary>The cleaned mod moves the rock, and disables it too or not; disabled, its winning version stands nowhere now.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARecordTheCleanedModDisabledIsRemovedInTheWinningPresence(bool disabled)
    {
        var moved = MoveInCleanedMod();
        if (disabled) moved.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

        Assert.Equal(disabled, Collect(Trust(laterTrusted: false)).WinningPresence.GetValueOrDefault(Rock).Removed);
    }

    /// <summary>The base game's rock is disabled or not; the cleaned mod moves it and enables it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARecordWhoseVersionBelowTheCleanedModIsDisabledHasARemovedPresenceBefore(bool disabledBefore)
    {
        if (disabledBefore) _rock.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
        var moved = MoveInCleanedMod();
        moved.MajorRecordFlagsRaw &= ~(int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

        var collected = Collect(Trust(laterTrusted: false));

        Assert.Equal((disabledBefore, false), (ChangedRecordOf(collected).PresenceBefore.Removed, collected.WinningPresence.GetValueOrDefault(Rock).Removed));
    }

    /// <summary>The base game's rock is enabled with the lever or the opposite of it; the cleaned mod moves the rock and gives it no parent.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnableParentOfTheVersionBelowTheCleanedModIsReadWithTheOppositeFlag(bool opposite)
    {
        var lever = PlaceLever();
        _rock.EnableParent = FixtureScenery.EnableParentOf(lever);
        if (opposite) _rock.EnableParent.Flags |= EnableParent.Flag.SetEnableStateToOppositeOfParent;
        MoveInCleanedMod().EnableParent = null;

        var collected = Collect(Trust(laterTrusted: false));

        Assert.Equal(new PlacedPresence(Removed: false, lever.FormKey.ToRecordKey(), opposite), ChangedRecordOf(collected).PresenceBefore);
        Assert.Equal(default, collected.WinningPresence.GetValueOrDefault(Rock));
    }

    /// <summary>The cleaned mod moves the rock, a primitive box on the trigger layer (12).</summary>
    [Fact]
    public void TheCollisionLayerAReferenceStoresIsReadWithWhatItSaysAboutItself()
    {
        var moved = MoveInCleanedMod();
        moved.Primitive = new PlacedPrimitive { Bounds = new P3Float(10, 10, 10) };
        moved.CollisionLayer = 12;

        var reference = ChangedRecordOf(Collect(Trust(laterTrusted: false))).ReferenceNow;

        Assert.Equal(((uint?)12, true), (reference.CollisionLayer, reference.IsPrimitive));
    }

    /// <summary>The cleaned mod moves the rock, a primitive box that carries multibound data, or room data, or none of them.</summary>
    [Theory]
    [InlineData(CullingData.None, false)]
    [InlineData(CullingData.MultiBound, true)]
    [InlineData(CullingData.LinkedRoom, true)]
    public void AReferenceCarryingRoomOrMultiboundDataIsReadAsACullingVolume(CullingData data, bool cullingVolume)
    {
        var moved = MoveInCleanedMod();
        moved.Primitive = new PlacedPrimitive { Bounds = new P3Float(10, 10, 10) };
        if (data == CullingData.MultiBound) moved.IsMultiBoundPrimitive = true;
        if (data == CullingData.LinkedRoom) moved.LinkedRooms.Add(new FormLink<IPlacedObjectGetter>(_rock.FormKey));

        var reference = ChangedRecordOf(Collect(Trust(laterTrusted: false))).ReferenceNow;

        Assert.Equal(cullingVolume, reference.IsCullingVolume);
    }

    public enum CullingData { None, MultiBound, LinkedRoom }

    /// <summary>An earlier patcher of the group moved the rock again, so the patch is the last to change its position.</summary>
    [Fact]
    public void AMoveThePatchChangedAgainIsLeftToThePatch()
    {
        var moved = MoveInCleanedMod();

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false), patch => Override(patch, moved).Placement!.Position = ToP3Float(MovedAgain)));

        Assert.Equal(PlacedParts.None, changed.Earlier.Undo);
        Assert.Equal([KeyValuePair.Create(PlacedParts.Position, Patch)], changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>
    /// The later plugin moves the rock away and the patch puts it back where the cleaned mod put it: in a Batch run, that is an
    /// earlier run's restore to the version below its own cleaned mod, so the cleaned mod's position still goes back.
    /// </summary>
    [Fact]
    public void AMoveThePatchSetsBackToTheCleanedModsPlaceAfterALaterPluginMovedItGoesBack()
    {
        var moved = MoveInCleanedMod();
        Override(_later.Mod, moved).Placement!.Position = ToP3Float(MovedAgain);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false), patch => Override(patch, moved)));

        Assert.Equal(PlacedParts.Position, changed.Earlier.Undo);
        Assert.Empty(changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>
    /// The cleaned mod moves the rock and takes away its enable parent; the later plugin carries both and makes the rock persistent,
    /// for example to fill a quest alias. When it loads without the cleaned mod, it set all three itself and keeps them. When it needs
    /// the cleaned mod, what it carries over keeps nothing, so the position and the presence go back; the Persistent flag the cleaned
    /// mod never changed stays as the later plugin set it.
    /// </summary>
    [Theory]
    [InlineData(false, PlacedParts.None, false)]
    [InlineData(true, PlacedParts.Position | PlacedParts.Presence, false)]
    public void APersistentFlagALaterPluginSetDoesNotGoBackWithThePresence(bool laterNeedsCleanedMod, PlacedParts undo, bool persistenceGoesBack)
    {
        _rock.EnableParent = FixtureScenery.EnableParentOf(PlaceLever());
        var moved = MoveInCleanedMod();
        moved.EnableParent = null;
        Override(_later.Mod, moved, persistent: true);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false, laterNeedsCleanedMod)));

        Assert.Equal((undo, persistenceGoesBack), (changed.Earlier.Undo, changed.Earlier.PersistenceGoesBack));
    }

    [Fact]
    public void APersistentFlagTheCleanedModSetGoesBackWithThePresence()
    {
        _rock.EnableParent = FixtureScenery.EnableParentOf(PlaceLever());
        var moved = Override(_cleaned.Mod, _rock, persistent: true);
        moved.Placement!.Position = ToP3Float(Moved);
        moved.EnableParent = null;

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.Position | PlacedParts.Presence, changed.Earlier.Undo);
        Assert.True(changed.Earlier.PersistenceGoesBack);
    }

    /// <summary>
    /// The base game's rock holds bytes the game library does not read, in an older record format than the cleaned mod's, which
    /// gives the rock another base: the base and data cannot be written back and stay, and that is kept as a fact of its own.
    /// </summary>
    [Fact]
    public void ABaseStoredInAnotherRecordFormatStaysAndIsKeptAsAFact()
    {
        _rock.FormVersion = OlderFormVersion;
        _rock.XWCN = new byte[] { 1 };
        var replaced = Override(_cleaned.Mod, _rock);
        replaced.FormVersion = NewerFormVersion;
        replaced.Base.SetTo(_base.AddMarkerStatic("OtherRockBase"));

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal((PlacedParts.None, PlacedParts.BaseAndData), (changed.Earlier.Undo, changed.Earlier.KeptForRecordFormat));
        Assert.Empty(changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>A deleted placed reference written into a plugin crashes the game, so a record whose earlier version is deleted never goes back.</summary>
    [Fact]
    public void ARecordWhoseVersionBelowTheCleanedModIsDeletedGivesNoChangedRecord()
    {
        _rock.IsDeleted = true;
        MoveInCleanedMod().IsDeleted = false;

        Assert.DoesNotContain(Collect(Trust(laterTrusted: false)).ChangedRecords, changed => changed.Key == Rock);
    }

    [Theory]
    [InlineData("position", PlacedParts.Position)]
    [InlineData("rotation", PlacedParts.Rotation)]
    [InlineData("scale", PlacedParts.Scale)]
    [InlineData("disabled", PlacedParts.Presence)]
    [InlineData("base", PlacedParts.BaseAndData)]
    [InlineData("radius", PlacedParts.BaseAndData)]
    public void EachPartTheCleanedModChangesGoesBackAlone(string change, PlacedParts part)
    {
        var version = Override(_cleaned.Mod, _rock);
        switch (change)
        {
            case "position": version.Placement!.Position = ToP3Float(Moved); break;
            case "rotation": version.Placement!.Rotation = new P3Float(0, 0, 1); break;
            case "scale": version.Scale = 2f; break;
            case "disabled": version.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled; break;
            case "base": version.Base.SetTo(_base.AddMarkerStatic("OtherRockBase")); break;
            case "radius": version.Radius = 50f; break;
        }

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(part, changed.Earlier.Undo);
        Assert.Empty(changed.Earlier.LeftToLaterPlugins);
    }

    /// <summary>A door's navmesh link goes back only with its navmesh, so it is no part of the placed record's facts.</summary>
    [Fact]
    public void ADoorLinkTheCleanedModChangesGivesNoChangedRecord()
    {
        Override(_cleaned.Mod, _rock).NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(_base.NextKey()), TeleportMarkerTriangle = 4 };

        Assert.DoesNotContain(Collect(Trust(laterTrusted: false)).ChangedRecords, changed => changed.Key == Rock);
    }

    /// <summary>
    /// The base game shows the rock in its removed form (Initially Disabled, no enable parent) and the cleaned mod shows it: where the
    /// cleaned mod was removed, the presence goes back like any other part, so the patch disables the rock again, as the game showed
    /// it without the cleaned mod.
    /// </summary>
    [Fact]
    public void ARockTheBaseGameShowsDisabledGoesBackDisabledWhereTheCleanedModWasRemoved()
    {
        _rock.MajorRecordFlagsRaw |= InitiallyDisabled;
        Override(_cleaned.Mod, _rock).MajorRecordFlagsRaw &= ~InitiallyDisabled;

        var (collected, patch) = RestoreEveryChangedRecord();

        Assert.Equal(PlacedParts.Presence, ChangedRecordOf(collected).Earlier.Undo);
        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        Assert.True(_rock.Equals(written));
    }

    /// <summary>
    /// The cleaned mod deleted a door with a navmesh link: the reader keeps the deleted winner, and the patch writes the door back
    /// whole into the base game's list, but without its navmesh link, which goes back only with its navmesh.
    /// </summary>
    [Fact]
    public void ADoorTheCleanedModDeletedComesBackWholeButWithoutItsNavmeshLink()
    {
        _rock.NavigationDoorLink = new NavigationDoorLink { NavMesh = new FormLink<INavigationMeshGetter>(_base.NextKey()), TeleportMarkerTriangle = 4 };
        var deleted = Override(_cleaned.Mod, _rock);
        deleted.IsDeleted = true;
        deleted.Placement = null;
        deleted.NavigationDoorLink = null;

        var (collected, patch) = RestoreEveryChangedRecord();

        Assert.True(ChangedRecordOf(collected).Earlier.Undo.HasFlag(PlacedParts.Presence));
        var written = Assert.Single(patch.EnumerateMajorRecords<IPlacedObjectGetter>());
        var expected = _rock.DeepCopy();
        expected.NavigationDoorLink = null;
        Assert.True(expected.Equals(written));
        var cell = Assert.Single(patch.EnumerateMajorRecords<ICellGetter>());
        Assert.Equal((_cell.FormKey, true), (cell.FormKey, cell.Temporary.Any(placed => placed.FormKey == _rock.FormKey)));
    }

    /// <summary>
    /// The fully trusted later plugin, which loads without the cleaned mod, holds the cleaned mod's position: it set that position, so
    /// the rock is no moved object and its position is left to it; the walk still reads past it down to the base game.
    /// </summary>
    [Fact]
    public void AFullyTrustedLaterPluginHoldingTheMoveKeepsThePositionAndTheTargetsAsTheyWere()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var collected = Collect(Trust(laterTrusted: true));

        var changed = ChangedRecordOf(collected);
        Assert.Equal((PlacedParts.None, (TargetId?)null), (changed.Earlier.Undo, changed.Target));
        Assert.Equal((_base.Key.ToPluginName(), Placed), (changed.Earlier.Plugin, changed.Earlier.Placement.Position));
        Assert.DoesNotContain(collected.Targets, target => target.Key == Rock);
    }

    [Fact]
    public void AMovedObjectsChangedRecordNamesItsTargetAndTheTargetUndoesThePosition()
    {
        MoveInCleanedMod();

        var collected = Collect(Trust(laterTrusted: false));

        var target = Assert.Single(collected.Targets, target => target.Key == Rock);
        Assert.Equal((TargetOrigin.Moved, PlacedParts.Position), (target.Origin, target.EarlierVersion!.Undo));
        Assert.Equal(target.Id, ChangedRecordOf(collected).Target);
    }

    [Fact]
    public void APlacedRecordOfTheCleanedModLinkingToTheRecordMarksItLinked()
    {
        MoveInCleanedMod();
        var lever = new PlacedObject(_cleaned.NextKey(), FixtureMod.Release) { Placement = new Placement(), EnableParent = new EnableParent() };
        lever.Base.SetTo(_cleaned.AddMarkerStatic("LeverBase"));
        lever.EnableParent.Reference.SetTo(_rock.FormKey);
        CellCopyIn(_cleaned.Mod).Temporary.Add(lever);

        Assert.True(ChangedRecordOf(Collect(Trust(laterTrusted: false))).LinkedFromCleanedMod);
    }

    [Fact]
    public void ANonPlacedRecordOfTheCleanedModLinkingToTheRecordMarksItLinked()
    {
        MoveInCleanedMod();
        var list = new FormList(_cleaned.NextKey(), FixtureMod.Release);
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(_rock.FormKey));
        _cleaned.Mod.FormLists.Add(list);

        Assert.True(ChangedRecordOf(Collect(Trust(laterTrusted: false))).LinkedFromCleanedMod);
    }

    [Fact]
    public void ARecordOfAnotherPluginLinkingToTheRecordDoesNotMarkItLinked()
    {
        MoveInCleanedMod();
        var list = new FormList(_later.NextKey(), FixtureMod.Release);
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(_rock.FormKey));
        _later.Mod.FormLists.Add(list);

        Assert.False(ChangedRecordOf(Collect(Trust(laterTrusted: false))).LinkedFromCleanedMod);
    }

    /// <summary>
    /// The changed records are read for links, but their plugins are not read: a trusted later plugin keeps the rock from being a
    /// target, so no plugin can link to a target, and the base game's rock adds the base game to the plugins read.
    /// </summary>
    [Fact]
    public void AChangedBaseGameRecordThatIsNoTargetDoesNotAddItsPluginToThePluginsTheLinksAreReadFor()
    {
        Override(_later.Mod, MoveInCleanedMod());
        UnreadableRecordPlugin? read = null;

        var collected = Collect(Trust(laterTrusted: true), null, records => read = new UnreadableRecordPlugin(records));

        Assert.Equal(Rock, ChangedRecordOf(collected).Key);
        Assert.Contains(Rock, read!.LinkedRecords);
        Assert.Empty(read.LinkedPlugins);
    }

    [Fact]
    public void TheLinksAreReadForThePluginsOfTheTargets()
    {
        MoveInCleanedMod();
        UnreadableRecordPlugin? read = null;

        Collect(Trust(laterTrusted: false), null, records => read = new UnreadableRecordPlugin(records));

        Assert.Equal([_base.Key.ToPluginName()], read!.LinkedPlugins);
    }

    [Fact]
    public void WithoutTheRestoreGroupTheLinksIntoAChangedRecordThatIsNoTargetAreNeitherReadNorMarked()
    {
        var read = CollectLinkedChangedRecordThatIsNoTarget(Plan with { Needs = Plan.Needs with { AreaRecordVersions = null } }, out var collected);

        Assert.False(ChangedRecordOf(collected).LinkedFromCleanedMod);
        Assert.DoesNotContain(Rock, read.LinkedRecords);
    }

    [Fact]
    public void WithTheRestoreGroupTheLinksIntoAChangedRecordThatIsNoTargetAreReadAndMarkIt()
    {
        var read = CollectLinkedChangedRecordThatIsNoTarget(Plan, out var collected);

        Assert.True(ChangedRecordOf(collected).LinkedFromCleanedMod);
        Assert.Contains(Rock, read.LinkedRecords);
    }

    /// <summary>A trusted later plugin keeps the rock from being a target, and a record of the cleaned mod links to it.</summary>
    private UnreadableRecordPlugin CollectLinkedChangedRecordThatIsNoTarget(WhatToCollect plan, out CollectedObjects collected)
    {
        Override(_later.Mod, MoveInCleanedMod());
        var list = new FormList(_cleaned.NextKey(), FixtureMod.Release);
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(_rock.FormKey));
        _cleaned.Mod.FormLists.Add(list);
        UnreadableRecordPlugin? read = null;

        collected = Collect(Trust(laterTrusted: true), null, records => read = new UnreadableRecordPlugin(records), plan);

        return read!;
    }

    /// <returns>The cleaned mod's version of the rock, moved.</returns>
    private PlacedObject MoveInCleanedMod()
    {
        var moved = Override(_cleaned.Mod, _rock);
        moved.Placement!.Position = ToP3Float(Moved);
        return moved;
    }

    /// <summary>The plugin's version of the rock: a copy of the given version in its copy of the rock's cell.</summary>
    /// <param name="persistent">The copy is made persistent and stands in the cell's persistent list; otherwise it stands in the temporary list as the given version is.</param>
    private PlacedObject Override(ISkyrimMod mod, PlacedObject from, bool persistent = false)
    {
        var version = (PlacedObject)from.DeepCopy();
        if (persistent)
        {
            version.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent;
            CellCopyIn(mod).Persistent.Add(version);
        }
        else CellCopyIn(mod).Temporary.Add(version);
        return version;
    }

    /// <summary>A base game lever in the rock's cell, which the rock can have as its enable parent.</summary>
    private PlacedObject PlaceLever()
    {
        var lever = new PlacedObject(_base.NextKey(), FixtureMod.Release) { Placement = new Placement() };
        lever.Base.SetTo(_base.AddMarkerStatic("LeverBase"));
        _cell.Temporary.Add(lever);
        return lever;
    }

    /// <summary>The plugin's copy of the rock's worldspace and cell, made on first use.</summary>
    private Cell CellCopyIn(ISkyrimMod mod)
    {
        if (_cellCopies.TryGetValue(mod.ModKey, out var copy)) return copy;
        copy = new Cell(_cell.FormKey, FixtureMod.Release) { Grid = _cell.Grid!.DeepCopy() };
        var worldspace = new Worldspace(_worldspace.FormKey, FixtureMod.Release);
        worldspace.AddCell(copy);
        mod.Worldspaces.Add(worldspace);
        _cellCopies.Add(mod.ModKey, copy);
        return copy;
    }

    /// <param name="writeIntoPatch">What an earlier patcher of the group wrote into the patch; nothing when null.</param>
    private CollectedObjects Collect(ModTrustTable trust, Action<ISkyrimMod>? writeIntoPatch = null) => Collect(trust, writeIntoPatch, records => records);

    /// <param name="wrapRecords">Gives the plugin records the collector reads, made from the real ones.</param>
    /// <param name="plan">What the run collects; the plan of the tests when null.</param>
    /// <param name="restoredBefore">The parts earlier runs gave back; none when null.</param>
    private CollectedObjects Collect(
        ModTrustTable trust, Action<ISkyrimMod>? writeIntoPatch, Func<IPluginRecords, IPluginRecords> wrapRecords, WhatToCollect? plan = null, ImmutableArray<RestoredPart>? restoredBefore = null)
    {
        var state = FixturePatcherState.Create(_belowLoadsBelowTheCleanedMod ? [_base.Mod, _below.Mod, _cleaned.Mod, _later.Mod] : [_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        writeIntoPatch?.Invoke(state.PatchMod);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        return PlacedObjects.Collect(wrapRecords(new PluginRecordsFactory(world, Environment.ProcessorCount).Open()), trust, plan ?? Plan, restoredBefore ?? []);
    }

    /// <summary>Collects with the later plugin untrusted, judges every changed record as lying in a cleared area and writes the patch.</summary>
    private (CollectedObjects Collected, ISkyrimModGetter Patch) RestoreEveryChangedRecord()
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        using var world = FixturePatcherState.CreateFinalWorld(state);
        var records = new PluginRecordsFactory(world, Environment.ProcessorCount).Open();
        var collected = PlacedObjects.Collect(records, Trust(laterTrusted: false), Plan);
        var cleared = TestAreaShares.Of(AreaVerdict.Cleared);
        var whereRemoved = new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            [.. collected.ChangedRecords.Select(changed => new PlacedChangeJudged(changed, null, cleared, null, Refused: null, IsRestored: true, ChangeJudge.KindOf(changed), KeptForArea: null, PlacedBack: null))],
            [], [], [], [], Passes: 1, DoorsLeftRemoved: [], Failures: [], RestoreWhereRemovedTimes.None);
        var protection = ObjectsToKeep.Build(collected.Targets, [], TestTargets.References(collected.Targets.Length), TestBaseFacts.Unresolved);
        var final = RemovalDecisions.Start(protection, [.. collected.Targets.Select(_ => (WhenRemoved)new WhenRemoved.Disable())]);
        PatchWriter.Write(new(final, collected.Targets, OtherModsObjectsResult.SwitchedOff, whereRemoved, records, new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText), _cleaned.Key.ToPluginName()));
        return (collected, state.PatchMod);
    }

    /// <param name="laterNeedsCleanedMod">The later plugin cannot load without the cleaned mod, as a patch for it; it then counts as trusted for it.</param>
    /// <param name="laterIsOtherGroupsPatch">The later plugin is the patch of another group, recognised by its marker.</param>
    private ModTrustTable Trust(bool laterTrusted, bool laterNeedsCleanedMod = false, bool laterIsOtherGroupsPatch = false)
    {
        var baseGame = _base.Key.ToPluginName();
        var later = _later.Key.ToPluginName();
        if (laterIsOtherGroupsPatch) return TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: [baseGame], untrusted: [], otherGroupPatches: [later]);
        return laterNeedsCleanedMod
            ? TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: [baseGame], untrusted: [], needingCleanedMod: [later])
            : TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: laterTrusted ? [baseGame, later] : [baseGame], untrusted: laterTrusted ? [] : [later]);
    }

    private RecordChangedByCleanedMod ChangedRecordOf(CollectedObjects collected) => Assert.Single(collected.ChangedRecords, changed => changed.Key == Rock);

    private static P3Float ToP3Float(Vector3 value) => new(value.X, value.Y, value.Z);
}
