using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsTrustAndReadsValidationTests
{
    [Fact]
    public void TheTrustListsHoldTheNamesAsTheSettingsSpellThemWithoutUnsetEntries()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension("PATCH.ESP"), ModKey.Null] };

        var ready = BuildReady(settings);

        Assert.Equal("PATCH.esp", Assert.Single(ready.Settings.ModTrust.TrustedModsAndTheirMasters).FileName);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void EachListedPluginThatIsNotInThePluginListGivesAWarningNamingItsList()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings
        {
            TrustedModsAndTheirMasters = [ModKey.FromNameAndExtension("City.esp")],
            ModsTrustedForOwnChangesOnly = [ModKey.FromNameAndExtension("Birds.esp")],
            NeverTrustedMods = [ModKey.FromNameAndExtension("Merged.esp")],
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            [
                "Warning: Which other mods can cause removals > Trusted mods, and their masters: City.esp is not in your plugin list.",
                "Warning: Which other mods can cause removals > Mods trusted for their own changes only: Birds.esp is not in your plugin list.",
                "Warning: Which other mods can cause removals > Never trusted mods: Merged.esp is not in your plugin list.",
            ],
            ready.Warnings.Select(warning => warning.Message));
        Assert.Equal("City.esp", Assert.Single(ready.Settings.ModTrust.TrustedModsAndTheirMasters).FileName);
    }

    [Fact]
    public void TheCleanedModOrThePatchListedAsNeverTrustedGivesAWarningEach()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { NeverTrustedMods = [ModKey.FromNameAndExtension("Target.esp"), ModKey.FromNameAndExtension("Patch.esp")] };

        var ready = BuildReady(settings);

        Assert.Equal(
            [
                "Warning: Which other mods can cause removals > Never trusted mods: Target.esp is the mod to clean up, which cannot be listed here; the entry has no effect.",
                "Warning: Which other mods can cause removals > Never trusted mods: Patch.esp is the patch this patcher writes, which cannot be listed here; the entry has no effect.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheTargetIsComparedAsTheLoadOrderSpellsItAndPrintedAsTheSettingsSpellIt()
    {
        var ready = BuildReady(SettingsFor("TARGET.ESP"));
        Assert.Equal("Target.esp", ready.Settings.Target.FileName);
        Assert.Equal("Target.esp", ready.Settings.ModTrust.Target.FileName);
        Assert.Equal("TARGET.esp", ready.Settings.TargetAsTyped.FileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditorIdAndOverriddenListsFollowTheDetailedLog(bool detailed)
    {
        var plan = BuildReady(SettingsFor(), detailedLogAndReports: detailed).Settings.Read;
        Assert.Equal(detailed, plan.Needs.OtherModDetails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDetailedLogAndReportsSettingTurnsOnTheDetailedLogTheReportFilesAndTheTouchChains(bool on)
    {
        var settings = BuildReady(SettingsFor(), detailedLogAndReports: on).Settings;

        Assert.Equal(on, settings.DetailedLog);
        Assert.Equal(on, settings.Reports is not null);
        Assert.Equal(on, settings.AlsoRemove.TouchChainsShown);
    }

    [Fact]
    public void NoReportsFolderIsNeededWhenTheDetailedLogAndReportsAreOff() =>
        Assert.Null(Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(
            StandardConfig.Of(SettingsFor()), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, reportsFolder: null, 1)).Settings.Reports);

    [Fact]
    public void AReportsFolderIsRequiredWhenTheDetailedLogAndReportsAreOn() =>
        Assert.Throws<ArgumentNullException>(() => SettingsValidator.Build(
            StandardConfig.Of(SettingsFor()), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, reportsFolder: null, 1));

    [Theory]
    [InlineData(nameof(NpcHandling.Never))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInsideTheObject))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision))]
    [InlineData(nameof(NpcHandling.AlwaysLikeAnyObject))]
    public void TheSelectStepHandlesNpcsAsTheTooCloseStepDoes(string handling)
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = Enum.Parse<NpcHandling>(handling);

        var ready = BuildReady(settings);

        Assert.Equal(Enum.Parse<NpcHandling>(handling), ready.Settings.Selection.Npcs);
        Assert.Equal(ready.Settings.Collision.TooClose.Npcs, ready.Settings.Selection.Npcs);
    }

    [Fact]
    public void AnInvalidNpcSettingGivesBothStepsTheDefault()
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = (NpcHandling)99;

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Selection.Npcs);
        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Collision.TooClose.Npcs);
    }

    [Fact]
    public void SurroundingsAreAlwaysReadAndNavmeshesOnlyForMarkerMovesOrTheRestoreGroup()
    {
        var relocating = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        relocating.TerrainChange.RemoveOnChangedTerrain = false;
        relocating.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouchingIt };
        relocating.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = true,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        TurnRestoreWhereRemovedOff(relocating);
        var runSettings = BuildReady(relocating).Settings;
        Assert.Equal(new WhatToCollect(new ScanNeeds(Navmesh: true), 10f, ProcessReplaced: false), runSettings.Read);
        Assert.True(runSettings.MarkerMoves.Runs);

        var neither = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        neither.TerrainChange.RemoveOnChangedTerrain = false;
        neither.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing };
        TurnRestoreWhereRemovedOff(neither);
        Assert.Equal(new WhatToCollect(new ScanNeeds(), 10f, ProcessReplaced: false), BuildReady(neither).Settings.Read);

        var restoring = WithoutChangedObjectsTerrainAndOldPlaces(SettingsFor());
        restoring.TerrainChange.RemoveOnChangedTerrain = false;
        restoring.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        restoring.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.Nothing };
        var restoringSettings = BuildReady(restoring).Settings;
        Assert.Equal(
            new WhatToCollect(
                new ScanNeeds(
                    Navmesh: true,
                    JudgeOldPlaces: true,
                    AreaRecordVersions: AreaReadsOf(restoringSettings),
                    DecideEveryObjectWithoutCleanedMod: true,
                    DecideEveryObjectBeforeCleanedMod: SettingDefaults.RestoreTerrain),
                10f,
                ProcessReplaced: false),
            restoringSettings.Read);
    }

    /// <summary>Only restoring other mods' objects where the cleaned mod was removed decides every object in the world without the cleaned mod.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void EveryObjectIsDecidedWithoutTheCleanedModOnlyWhenOtherModsObjectsAreRestoredWhereItWasRemoved(bool otherModsObjects, bool decided)
    {
        var settings = SettingsFor();
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = otherModsObjects;

        Assert.Equal(decided, BuildReady(settings).Settings.Read.Needs.DecideEveryObjectWithoutCleanedMod);
    }

    private static void TurnRestoreWhereRemovedOff(StandardSettings settings)
    {
        settings.RestoreWhereRemoved.RestoreOtherModsObjects = false;
        settings.RestoreWhereRemoved.RestoreTerrain = false;
        settings.RestoreWhereRemoved.RestoreCells = false;
        settings.RestoreWhereRemoved.RestoreNavmeshes = false;
    }

    /// <remarks>Every restore switch is on by default, so every kind is read.</remarks>
    private static AreaRecordReads AreaReadsOf(RunSettings settings) =>
        new(new AreaPartTolerances(settings.RestoreWhereRemoved!.HeightChangeTolerance, settings.RestoreWhereRemoved.WaterHeightChangeTolerance), Cells: true, Lands: true);

    /// <summary>An object counts as resting only within the touch gap, so the drop test stops at the first distance within it without measuring up to the pull limit.</summary>
    [Fact]
    public void TheLargestTouchGapIsBelowTheLimitOtherModsObjectsArePulledDownBy() =>
        Assert.True(AlsoRemoveSettingsBuilder.MaxTouchDistance < OtherModsObjectsLimits.MaxPullDistance);

    [Fact]
    public void OtherModsObjectsAreJudgedByTheGapAndShareOfTheAlsoRemoveGroup()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = FollowUpRemovalMode.Nothing,
            TouchDistance = 12f,
            AnchoringThresholdPercent = 40f,
            OtherModsObjectsLeftWithoutSupport = OtherModsObjectsHandling.RemoveFloatingObject,
        };

        var otherModsObjects = BuildReady(settings).Settings.OtherModsObjects;

        Assert.Equal(new OtherModsObjectsSettings(OtherModsObjectsHandling.RemoveFloatingObject, 12f, 0.4f), otherModsObjects);
    }

    [Fact]
    public void OtherModsObjectsArePulledToTheGroundByDefault() =>
        Assert.Equal(OtherModsObjectsHandling.PullObjectToTheGround, BuildReady(SettingsFor()).Settings.OtherModsObjects.Handling);

    [Fact]
    public void AnUndefinedOtherModsObjectsValueIsReplacedByTheDefaultWithAWarning()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval.OtherModsObjectsLeftWithoutSupport = (OtherModsObjectsHandling)99;

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.OtherModsObjects, ready.Settings.OtherModsObjects.Handling);
        Assert.Contains(ready.Warnings, warning => warning.Message.Contains("Other mods' objects left without support", StringComparison.Ordinal));
    }
}
