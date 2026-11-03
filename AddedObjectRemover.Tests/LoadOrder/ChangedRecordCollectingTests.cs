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
    private readonly Worldspace _worldspace;
    private readonly Cell _cell;
    private readonly PlacedObject _rock;
    private readonly Dictionary<ModKey, Cell> _cellCopies = [];

    public ChangedRecordCollectingTests()
    {
        _cleaned = new FixtureMod("Cleaned.esp", _base);
        _later = new FixtureMod("Later.esp", _base);
        _cell = new Cell(_base.NextKey(), FixtureMod.Release) { Grid = new CellGrid { Point = new P2Int(0, 0) } };
        _worldspace = new Worldspace(_base.NextKey(), FixtureMod.Release);
        _worldspace.AddCell(_cell);
        _base.Mod.Worldspaces.Add(_worldspace);
        _rock = new PlacedObject(_base.NextKey(), FixtureMod.Release) { Placement = new Placement { Position = ToP3Float(Placed) } };
        _rock.Base.SetTo(_base.AddMarkerStatic("RockBase"));
        _cell.Temporary.Add(_rock);
    }

    private RecordKey Rock => _rock.FormKey.ToRecordKey();

    [Fact]
    public void AMoveALaterPluginOnlyCarriesOverGoesBackToTheVersionBelowTheCleanedMod()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.Position, changed.Earlier.Undo);
        Assert.Empty(changed.Earlier.LeftToLaterPlugins);
        Assert.Equal((_base.Key.ToPluginName(), Placed), (changed.Earlier.Plugin, changed.Earlier.Placement.Position));
        Assert.Equal((_later.Key.ToPluginName(), (Vector3?)Moved), (changed.WinningMod, changed.WhereItIsNow?.Position));
    }

    [Fact]
    public void AMoveALaterPluginMovesAgainIsLeftToThatPlugin()
    {
        Override(_later.Mod, MoveInCleanedMod()).Placement!.Position = ToP3Float(MovedAgain);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.None, changed.Earlier.Undo);
        Assert.Equal([KeyValuePair.Create(PlacedParts.Position, _later.Key.ToPluginName())], changed.Earlier.LeftToLaterPlugins);
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

    /// <summary>The later plugin carries the move over and disables the rock: a record removed after the cleaned mod gets nothing back.</summary>
    [Fact]
    public void NothingGoesBackInARecordALaterPluginDisables()
    {
        Override(_later.Mod, MoveInCleanedMod()).MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

        Assert.DoesNotContain(Collect(Trust(laterTrusted: false)).ChangedRecords, changed => changed.Key == Rock);
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
    /// for example to fill a quest alias. The Persistent flag stays the later plugin's.
    /// </summary>
    [Fact]
    public void APersistentFlagALaterPluginSetDoesNotGoBackWithThePresence()
    {
        _rock.EnableParent = FixtureScenery.EnableParentOf(PlaceLever());
        var moved = MoveInCleanedMod();
        moved.EnableParent = null;
        Override(_later.Mod, moved, persistent: true);

        var changed = ChangedRecordOf(Collect(Trust(laterTrusted: false)));

        Assert.Equal(PlacedParts.Position | PlacedParts.Presence, changed.Earlier.Undo);
        Assert.False(changed.Earlier.PersistenceGoesBack);
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
    /// The fully trusted later plugin decides the record, so the rock is no moved object, as without the changed-record facts; the walk
    /// still reads past it down to the base game, and its carried-over move is no change.
    /// </summary>
    [Fact]
    public void AFullyTrustedLaterPluginCarryingTheMoveLeavesThePositionToGoBackAndTheTargetsAsTheyWere()
    {
        Override(_later.Mod, MoveInCleanedMod());

        var collected = Collect(Trust(laterTrusted: true));

        var changed = ChangedRecordOf(collected);
        Assert.Equal((PlacedParts.Position, (TargetId?)null), (changed.Earlier.Undo, changed.Target));
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
    private CollectedObjects Collect(
        ModTrustTable trust, Action<ISkyrimMod>? writeIntoPatch, Func<IPluginRecords, IPluginRecords> wrapRecords, WhatToCollect? plan = null)
    {
        var state = FixturePatcherState.Create([_base.Mod, _cleaned.Mod, _later.Mod], Path.GetTempPath(), Path.GetTempPath());
        writeIntoPatch?.Invoke(state.PatchMod);
        using var world = FixturePatcherState.CreateFinalWorld(state);
        return PlacedObjects.Collect(wrapRecords(new PluginRecordsFactory(world, Environment.ProcessorCount).Open()), trust, plan ?? Plan);
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
        PatchWriter.Write(new(final, collected.Targets, OtherModsObjectsResult.SwitchedOff, whereRemoved, records, new PatchWriting(new SynthesisWriteCache(state.LinkCache), state.PatchMod, Program.PatchMarkerText)));
        return (collected, state.PatchMod);
    }

    private ModTrustTable Trust(bool laterTrusted)
    {
        var baseGame = _base.Key.ToPluginName();
        var later = _later.Key.ToPluginName();
        return TestModTrust.Of(_cleaned.Key.ToPluginName(), Patch, trusted: laterTrusted ? [baseGame, later] : [baseGame], untrusted: laterTrusted ? [] : [later]);
    }

    private RecordChangedByCleanedMod ChangedRecordOf(CollectedObjects collected) => Assert.Single(collected.ChangedRecords, changed => changed.Key == Rock);

    private static P3Float ToP3Float(Vector3 value) => new(value.X, value.Y, value.Z);
}
