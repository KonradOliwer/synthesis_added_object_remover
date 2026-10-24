using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class RunSettingsValidationTests
{
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private const string ReportsFolder = "/out";

    private static LoadOrderPlugins Facts(bool targetLoaded = true) =>
        TestLoadOrderPlugins.Of(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, targetLoaded, []), new PluginListing(Patch, true, [])]);

    /// <summary>The target only has to be in the plugin list; the plugins above the patch are the ones Synthesis loads.</summary>
    private static LoadOrderPlugins FactsWithPluginList(PluginName[] pluginList, params PluginName[] disabled) =>
        TestLoadOrderPlugins.Of(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Patch, true, [])],
            pluginList,
            disabled);

    private static readonly PluginName Base = new("Skyrim.esm");
    private static readonly PluginName Middle = new("Middle.esp");
    private static readonly LoadOrderPlugins DisabledTargetFacts = FactsWithPluginList([Base, Target, Patch], Target);
    private static readonly LoadOrderPlugins NotLoadedTargetFacts = FactsWithPluginList([Base, Target, Patch]);
    private static readonly LoadOrderPlugins TargetAfterPatchFacts = FactsWithPluginList([Base, Patch, Middle, Target]);

    private static StandardSettings SettingsFor(string target = "Target.esp") =>
        new() { CleanedMod = ModKey.FromNameAndExtension(target) };

    private static RunRequest Build(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, workers);

    private static RunRequest.Ready BuildReady(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        Assert.IsType<RunRequest.Ready>(Build(settings, facts, detailedLogAndReports, workers));

    private static BatchRequest BuildAll(ConfigsRead read, LoadOrderPlugins? facts = null) =>
        SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1);

    [Fact]
    public void AnEmptyCleanedModStopsTheRunAsNoTargetSet()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(new StandardSettings()));
        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), stop.Reason);
    }

    [Fact]
    public void ATargetNotInThePluginListStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor("Missing.esp")));
        Assert.Equal(new StopReason(StopKind.CleanedModNotInLoadOrder, "Missing.esp"), stop.Reason);
    }

    [Fact]
    public void ADisabledTargetStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), DisabledTargetFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModDisabled, "Target.esp"), stop.Reason);
    }

    [Fact]
    public void ATargetEnabledAboveThePatchButNotLoadedBySynthesisStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), NotLoadedTargetFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModNotLoadedBySynthesis, "Target.esp"), stop.Reason);
    }

    [Fact]
    public void ATargetLoadingAfterThePatchStopsTheRunWithBothPositions()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor(), TargetAfterPatchFacts));
        Assert.Equal(new StopReason(StopKind.CleanedModLoadsAfterPatch, "Target.esp", new PatchPlacement("Patch.esp", "Target.esp", 3, 1)), stop.Reason);
    }

    [Fact]
    public void ThePatchPluginAsTargetStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(SettingsFor("Patch.esp")));
        Assert.Equal(new StopReason(StopKind.TargetIsThePatch, "Patch.esp"), stop.Reason);
    }

    [Fact]
    public void TooCloseWithNeitherMarginOnGivesAWarning()
    {
        var settings = SettingsFor();
        settings.TooClose.BySize.Use = false;
        settings.TooClose.ByDistance.Use = false;

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.TooClose.BySize || ready.Settings.TooClose.ByDistance);
        Assert.Equal(
            "Warning: neither too-close margin is on; no object is removed for being too close.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void ValidDefaultSettingsGiveNoWarnings()
    {
        var ready = BuildReady(SettingsFor());
        Assert.Empty(ready.Warnings);
        Assert.Equal(Target, ready.Settings.Target);
        Assert.Equal(3, ready.Settings.Execution.Workers);
    }

    [Fact]
    public void AnUnreadableTargetGivesAWarning()
    {
        var ready = BuildReady(SettingsFor(), Facts(targetLoaded: false));
        Assert.Equal("Warning: could not read Target.esp to find its masters.", Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void EachEnabledPluginWithoutItsFileGivesAWarningLine()
    {
        var facts = Facts() with { MissingPlugins = [new PluginName("Gone.esp"), new PluginName("Lost.esm")] };

        var ready = BuildReady(SettingsFor(), facts);

        Assert.Equal(
            [
                "Warning: Gone.esp is enabled in your plugin list but its file was not found; it was skipped.",
                "Warning: Lost.esm is enabled in your plugin list but its file was not found; it was skipped.",
            ],
            ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void WarningsComeInTheOrderSettingsAreCheckedWithEachInvalidValueReportedOnce()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { MaxOtherMastersOfRequiringModToTrust = 500 };
        settings.TooClose.NpcHandling = (NpcHandling)99;
        settings.TooClose.BySize.SizeMultiplier = 9f;
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = (FollowUpRemovalMode)99,
            TouchDistance = 100f,
            AnchoringThresholdPercent = 500f,
        };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            SurroundingObjects = new SurroundingObjectsSettings
            {
                SearchRadius = 1f,
                DirectionThresholdPercent = 45,
                RemovedDirectionsPercent = 44,
                OccupiedDirectionsPercent = 5,
            },
        };

        var messages = BuildReady(settings, Facts(targetLoaded: false)).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: could not read Target.esp",
            "Warning: Which other mods can cause removals > ...only if it has at most this many other masters 500 is outside 0-100",
            "Warning: Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal",
            "Warning: Remove objects too close to other mods' objects > Margin by object size > Margin on every side",
            "Warning: Also remove what rests on removed objects > When an object is removed, also remove",
            "Warning: Also remove what rests on removed objects > Gap that still counts as touching",
            "Warning: Also remove what rests on removed objects > OnlyWhatLosesItsSupport: remove when this much support is gone",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Look this far around it",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > A direction is cleared when this much of it was removed 45 is not a multiple",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Remove when this share of those directions is cleared 44 is not a multiple",
            "Warning: Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Decide only if this share of the 8 directions has objects 5 is outside",
        ];
        Assert.Equal(expectedStarts.Length, messages.Count);
        for (var i = 0; i < expectedStarts.Length; i++)
        {
            Assert.StartsWith(expectedStarts[i], messages[i]);
        }
    }

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
        Assert.Equal(detailed, plan.OtherModDetails);
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
        Assert.Equal(ready.Settings.TooClose.Npcs, ready.Settings.Selection.Npcs);
    }

    [Fact]
    public void AnInvalidNpcSettingGivesBothStepsTheDefault()
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = (NpcHandling)99;

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Selection.Npcs);
        Assert.Equal(SettingDefaults.Npcs, ready.Settings.TooClose.Npcs);
    }

    [Fact]
    public void SurroundingsAreReadForSupportModeAndNavmeshesOnlyForMarkerMoves()
    {
        var supportOnly = SettingsFor();
        supportOnly.TerrainChange.RemoveOnChangedTerrain = false;
        supportOnly.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        supportOnly.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.OnlyWhatLosesItsSupport };
        Assert.Equal(new WhatToCollect(true, true, false, false, false, false, false), BuildReady(supportOnly).Settings.Read);

        var relocating = SettingsFor();
        relocating.TerrainChange.RemoveOnChangedTerrain = false;
        relocating.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouchingIt };
        relocating.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = true,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        var runSettings = BuildReady(relocating).Settings;
        Assert.Equal(new WhatToCollect(true, true, true, false, false, false, false), runSettings.Read);
        Assert.NotNull(runSettings.MarkerMoves);

        var neither = SettingsFor();
        neither.TerrainChange.RemoveOnChangedTerrain = false;
        neither.LeftoverInvisibleObjects.MoveKeptMarkersOutOfOtherModsObjects = false;
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouchingIt };
        Assert.Equal(new WhatToCollect(false, false, false, false, false, false, false), BuildReady(neither).Settings.Read);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void WaterIsReadOnlyForTheChangedTerrainStepCountingAWaterChange(bool on, bool waterHeights, bool waterAddedOrRemoved, bool water)
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = on,
            CountWaterHeightChanges = waterHeights,
            CountWaterAddedOrRemoved = waterAddedOrRemoved,
        };

        Assert.Equal(water, BuildReady(settings).Settings.Read.Water);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, true)]
    public void TerrainVersionsAreReadOnlyForTheChangedTerrainStepAndLayersOnlyForTerrainTypes(bool on, bool types, bool versions, bool layers)
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = on, CountTerrainTypeChanges = types };

        var read = BuildReady(settings).Settings.Read;

        Assert.Equal((versions, layers), (read.TerrainVersions, read.TerrainLayers));
    }

    [Fact]
    public void TheChangedTerrainSettingsAreOnByDefaultAndPassedThrough()
    {
        var defaults = BuildReady(SettingsFor()).Settings.ChangedTerrain;
        Assert.Equal(
            new ChangedTerrainSettings(
                On: true, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, CountHeightChanges: true, HeightTolerance: 16f, CountTerrainTypeChanges: true,
                MinTerrainTypeChangePercent: 20, CountWaterHeightChanges: true, WaterHeightTolerance: 16f, CountWaterAddedOrRemoved: true,
                MinWaterCoverChangePercent: 20, AreaBySize: true, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 64f),
            defaults);

        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes,
            CountHeightChanges = false,
            HeightChangeTolerance = 0f,
            MinTerrainTypeChangePercent = 50,
            CountWaterHeightChanges = false,
            WaterHeightChangeTolerance = 32f,
            CountWaterAddedOrRemoved = false,
            MinWaterCoverChangePercent = 40,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 2f },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 128f },
        };
        var ready = BuildReady(settings);

        Assert.Empty(ready.Warnings);
        Assert.Equal(
            new ChangedTerrainSettings(
                On: true, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, false, 0f, true, 50, false, 32f, false, 40,
                AreaBySize: true, AreaMultiplier: 2f, AreaByDistance: true, AreaDistanceInUnits: 128f),
            ready.Settings.ChangedTerrain);
    }

    [Fact]
    public void ChangedTerrainWithNeitherTerrainMarginOnIsSwitchedOffWithAWarning()
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            BySize = new TerrainBySizeSettings { Use = false },
            ByDistance = new TerrainByDistanceSettings { Use = false },
        };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedTerrain.On);
        Assert.False(ready.Settings.Read.TerrainVersions);
        Assert.Equal(
            "Warning: changed terrain is on, but neither terrain margin is on; no object is removed for changed terrain.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void TerrainMarginsAboveTheirLimitsAreClampedWithAWarningEach()
    {
        var settings = SettingsFor();
        settings.TerrainChange.BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 9f };
        settings.TerrainChange.ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 5000f };

        var ready = BuildReady(settings);

        Assert.Equal(5f, ready.Settings.ChangedTerrain.AreaMultiplier);
        Assert.Equal(1024f, ready.Settings.ChangedTerrain.AreaDistanceInUnits);
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Margin by object size > Margin on every side 9",
            "Warning: Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side 5000",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(100)]
    public void AGroundTypePercentFromZeroTo100IsKeptWithoutAWarning(int percent)
    {
        var settings = SettingsFor();
        settings.TerrainChange.MinTerrainTypeChangePercent = percent;

        var ready = BuildReady(settings);

        Assert.Equal(percent, ready.Settings.ChangedTerrain.MinTerrainTypeChangePercent);
        Assert.Empty(ready.Warnings);
    }

    [Theory]
    [InlineData(-1, 0, "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type -1 is outside 0-100")]
    [InlineData(101, 100, "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground points changed type 101 is outside 0-100")]
    public void AGroundTypePercentOutsideZeroTo100IsCorrectedWithAWarning(int percent, int expected, string expectedStart)
    {
        var settings = SettingsFor();
        settings.TerrainChange.MinTerrainTypeChangePercent = percent;

        var ready = BuildReady(settings);

        Assert.Equal(expected, ready.Settings.ChangedTerrain.MinTerrainTypeChangePercent);
        Assert.StartsWith(expectedStart, Assert.Single(ready.Warnings).Message);
    }

    [Theory]
    [InlineData(false, ZoneShape.ObjectShape)]
    [InlineData(true, ZoneShape.BoundingBox)]
    public void RoughBoxesChooseTheBoundingBoxZoneShape(bool useRoughBoxes, ZoneShape expected)
    {
        var settings = SettingsFor();
        settings.TooClose.UseRoughBoxes = useRoughBoxes;

        Assert.Equal(expected, BuildReady(settings).Settings.TooClose.Zone);
    }

    [Fact]
    public void TickedAlwaysKeepKindsAreTheKindsNeverRemoved()
    {
        var settings = SettingsFor();
        settings.LeftoverInvisibleObjects.AlwaysKeep = new KeptInvisibleObjectSettings { Lights = true, SoundMarkers = true };

        var neverRemove = BuildReady(settings).Settings.LeftBehind!.NeverRemove;

        Assert.Equal(new[] { InvisibleObjectKind.Lights, InvisibleObjectKind.SoundMarkers }, neverRemove.Order());
    }

    [Fact]
    public void NothingIsKeptUntilAKindIsTicked()
    {
        Assert.Empty(BuildReady(SettingsFor()).Settings.LeftBehind!.NeverRemove);
    }

    [Fact]
    public void ChangedTerrainCountingNoChangeIsSwitchedOffWithAWarning()
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            CountHeightChanges = false,
            CountTerrainTypeChanges = false,
            CountWaterHeightChanges = false,
            CountWaterAddedOrRemoved = false,
        };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedTerrain.On);
        Assert.False(ready.Settings.Read.TerrainVersions);
        Assert.Equal(
            "Warning: changed terrain is on, but no height, ground type or water changes are counted; no object is removed for changed terrain.",
            Assert.Single(ready.Warnings).Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ChangedTerrainCountingOnlyAWaterChangeStaysOn(bool waterHeights, bool waterAddedOrRemoved)
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            CountHeightChanges = false,
            CountTerrainTypeChanges = false,
            CountWaterHeightChanges = waterHeights,
            CountWaterAddedOrRemoved = waterAddedOrRemoved,
        };

        var ready = BuildReady(settings);

        Assert.True(ready.Settings.ChangedTerrain.On);
        Assert.True(ready.Settings.Read.TerrainVersions);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void InvalidWaterValuesAreCorrectedWithAWarningEach()
    {
        var settings = SettingsFor();
        settings.TerrainChange.WaterHeightChangeTolerance = -5f;
        settings.TerrainChange.MinWaterCoverChangePercent = 101;

        var ready = BuildReady(settings);

        Assert.Equal((0f, 100), (ready.Settings.ChangedTerrain.WaterHeightTolerance, ready.Settings.ChangedTerrain.MinWaterCoverChangePercent));
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Count only water height changes larger than -5",
            "Warning: Remove objects on terrain changed by other mods > Remove only when this share of the ground got covered/uncovered 101 is outside 0-100",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Fact]
    public void InvalidChangedTerrainValuesAreCorrectedEvenWhenTheStepIsOff()
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = false,
            TerrainCheckedObjects = (TerrainCheckedObjects)99,
            HeightChangeTolerance = 300f,
            BySize = new TerrainBySizeSettings { SizeMultiplier = float.NaN },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = -1f },
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            new ChangedTerrainSettings(
                On: false, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, true, 256f, true, 20, true, 16f, true, 20,
                AreaBySize: true, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 0f),
            ready.Settings.ChangedTerrain);
        string[] expectedStarts =
        [
            "Warning: Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked",
            "Warning: Remove objects on terrain changed by other mods > Count only height changes larger than 300",
            "Warning: Remove objects on terrain changed by other mods > Margin by object size > Margin on every side is not a valid number",
            "Warning: Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side -1",
        ];
        Assert.Equal(expectedStarts.Length, ready.Warnings.Length);
        Assert.All(expectedStarts.Zip(ready.Warnings), pair => Assert.StartsWith(pair.First, pair.Second.Message));
    }

    [Fact]
    public void MarkerMovesNeedTheLeftBehindStep()
    {
        var settings = SettingsFor();
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = false,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        var runSettings = BuildReady(settings).Settings;
        Assert.Null(runSettings.MarkerMoves);
        Assert.Null(runSettings.LeftBehind);
    }

    [Fact]
    public void SupportThresholdBecomesAFraction()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings { AnchoringThresholdPercent = 25f };
        Assert.Equal(0.25f, BuildReady(settings).Settings.AlsoRemove.SupportLostFraction);
    }

    [Fact]
    public void StandardReportsGoToTheReportsFolderItself() =>
        Assert.Equal(ReportsFolder, BuildReady(SettingsFor(), detailedLogAndReports: true).Settings.Reports?.Folder);

    [Fact]
    public void AConfigFilesReportsGoToASubfolderNamedAfterTheConfig()
    {
        var read = new ConfigsRead.Ready([new ConfiguredSettings(ConfigSource("town_walls", 1, 1), SettingsFor())]);
        var batch = Assert.IsType<BatchRequest.Ready>(SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1));
        var ready = Assert.IsType<RunRequest.Ready>(Assert.Single(batch.Requests));

        Assert.Equal(Path.Combine(ReportsFolder, "town_walls"), ready.Settings.Reports?.Folder);
    }

    [Fact]
    public void TheSourceReachesTheRequest()
    {
        var source = ConfigSource("town_walls", 1, 2);

        var request = Assert.Single(BuildBatch(source, SettingsFor()).Requests);

        Assert.Same(source, request.Source);
        Assert.IsType<SettingsSource.Standard>(BuildReady(SettingsFor()).Source);
    }

    [Fact]
    public void AReadFailurePassesThroughUnchanged()
    {
        var failed = BuildAll(new ConfigsRead.Failed("Group file x is missing."));

        Assert.Equal("Group file x is missing.", Assert.IsType<BatchRequest.Failed>(failed).Message);
    }

    [Fact]
    public void AConfigFileWithoutATargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), new StandardSettings());

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")} sets no mod to clean up (CleanedMod). Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotInThePluginListFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Missing.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Missing.esp is not in your plugin list. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithADisabledTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), DisabledTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is disabled. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotLoadedBySynthesisFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), NotLoadedTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is enabled in your plugin list but Synthesis did not load it for this group. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetLoadingAfterThePatchFailsTheBatchNamingTheFileAndBothPositions()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), TargetAfterPatchFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp loads after the patch Patch.esp (position 3 > 1): move Patch.esp below it. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithThePatchPluginAsTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Patch.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Patch.esp is the patch this patcher writes. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void TheFirstConfigFileThatStopsIsTheOneReportedInGroupOrder()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 3), SettingsFor()),
            new ConfiguredSettings(ConfigSource("second", 2, 3), new StandardSettings()),
            new ConfiguredSettings(ConfigSource("third", 3, 3), SettingsFor("Missing.esp")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.StartsWith($"Config file {FilePathOf("second")} sets no mod", failed.Message);
    }

    [Fact]
    public void TwoConfigFilesWithTheSameTargetFailTheBatchNamingBothFiles()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 2), SettingsFor("Target.esp")),
            new ConfiguredSettings(ConfigSource("second", 2, 2), SettingsFor("target.ESP")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.Equal(
            $"Config files {FilePathOf("first")} and {FilePathOf("second")} both clean up Target.esp; each mod may be cleaned up by one config only. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AStandardRequestThatStopsStaysAStopRequest()
    {
        var read = new ConfigsRead.Ready([StandardConfig.Of(new StandardSettings())]);

        var batch = Assert.IsType<BatchRequest.Ready>(BuildAll(read));

        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), Assert.IsType<RunRequest.Stop>(Assert.Single(batch.Requests)).Reason);
    }

    private static string FilePathOf(string name) => Path.Combine("configs", name + ".json");

    private static SettingsSource.ConfigFile ConfigSource(string name, int number, int count) =>
        new(name, name + ".json", FilePathOf(name), number, count, new ConfigFileNotes([], [], []));

    private static BatchRequest.Ready BuildBatch(StandardSettings settings) => BuildBatch(ConfigSource("town_walls", 1, 1), settings);

    private static BatchRequest.Ready BuildBatch(SettingsSource source, StandardSettings settings) =>
        Assert.IsType<BatchRequest.Ready>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)])));

    private static BatchRequest.Failed FailedBatch(SettingsSource source, StandardSettings settings, LoadOrderPlugins? facts = null) =>
        Assert.IsType<BatchRequest.Failed>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)]), facts));

    [Theory]
    [InlineData(50, 50)]
    [InlineData(44, 40)]
    [InlineData(45, 50)]
    [InlineData(5, 10)]
    [InlineData(105, 100)]
    public void PercentIsClampedAndRoundedToWholeTens(int percent, int expected) =>
        Assert.Equal(expected, SettingCorrections.WholeTens(percent, "test percent", new List<SettingWarning>()));

    [Fact]
    public void ANumberThatIsNotANumberBecomesTheDefaultWithAWarning()
    {
        var warnings = new List<SettingWarning>();
        Assert.Equal(0.5f, SettingCorrections.Clamp(float.NaN, 0f, 5f, 0.5f, "test number", warnings));
        Assert.Equal("Warning: test number is not a valid number; using 0.5.", Assert.Single(warnings).Message);
    }
}
