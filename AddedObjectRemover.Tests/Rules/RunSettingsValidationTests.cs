using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

public class RunSettingsValidationTests
{
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private const string ReportBaseFolder = "/out";

    private static LoadOrderPlugins Facts(bool targetLoaded = true) =>
        new(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, targetLoaded, []), new PluginListing(Patch, true, [])]);

    private static StandardSettings SettingsFor(string target = "Target.esp") =>
        new() { TargetPlugin = ModKey.FromNameAndExtension(target) };

    private static RunRequest.Ready BuildReady(StandardSettings settings, LoadOrderPlugins? facts = null, int workers = 3) =>
        Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), facts ?? Facts(), ReportBaseFolder, workers));

    [Fact]
    public void AnEmptyTargetPluginStopsTheRunAsNoTargetSet()
    {
        var stop = Assert.IsType<RunRequest.Stop>(SettingsValidator.Build(StandardConfig.Of(new StandardSettings()), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));
        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), stop.Reason);
    }

    [Fact]
    public void ATargetOutsideTheLoadOrderStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(SettingsValidator.Build(StandardConfig.Of(SettingsFor("Missing.esp")), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));
        Assert.Equal(new StopReason(StopKind.TargetNotInLoadOrder, "Missing.esp"), stop.Reason);
    }

    [Fact]
    public void ThePatchPluginAsTargetStopsTheRun()
    {
        var stop = Assert.IsType<RunRequest.Stop>(SettingsValidator.Build(StandardConfig.Of(SettingsFor("Patch.esp")), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));
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
    public void WarningsComeInTheOrderSettingsAreCheckedWithEachInvalidValueReportedOnce()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings
        {
            CompatibilityPatches = new CompatibilityPatchSettings { MaxOtherMastersForPatch = 500 },
        };
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
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "bad\0folder" };

        var messages = BuildReady(settings, Facts(targetLoaded: false)).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: could not read Target.esp",
            "Warning: maximum other masters for a patch",
            "Warning: NPCs and creatures setting",
            "Warning: Remove objects too close to other mods' objects > Margin by object size > Margin on every side",
            "Warning: follow-up removal mode",
            "Warning: touch distance",
            "Warning: anchoring threshold",
            "Warning: search radius",
            "Warning: removed area per direction 45 is not a multiple",
            "Warning: removed directions required 44 is not a multiple",
            "Warning: occupied directions required 5 is outside",
            "Warning: report folder",
        ];
        Assert.Equal(expectedStarts.Length, messages.Count);
        for (var i = 0; i < expectedStarts.Length; i++)
        {
            Assert.StartsWith(expectedStarts[i], messages[i]);
        }
    }

    [Fact]
    public void ExcludedNamesHoldTheParsedKeys()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = [ModKey.FromNameAndExtension("Other.esp"), ModKey.Null] };
        Assert.Equal("Other.esp", Assert.Single(BuildReady(settings).Settings.ExcludedNames));
    }

    [Fact]
    public void TheTargetIsComparedAsTheLoadOrderSpellsItAndPrintedAsTheSettingsSpellIt()
    {
        var ready = BuildReady(SettingsFor("TARGET.ESP"));
        Assert.Equal("Target.esp", ready.Settings.Target.FileName);
        Assert.Equal("Target.esp", ready.Settings.ModIdentification.Target.FileName);
        Assert.Equal("TARGET.esp", ready.Settings.TargetAsTyped.FileName);
    }

    [Fact]
    public void KnownExcludedPluginsAreSpelledAsTheLoadOrderListsThem()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = [ModKey.FromNameAndExtension("PATCH.ESP"), ModKey.FromNameAndExtension("Unknown.esp")] };
        Assert.Equal("Patch.esp", Assert.Single(BuildReady(settings).Settings.ModIdentification.Excluded).FileName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EditorIdAndOverriddenListsFollowTheDetailedLog(bool detailed, bool expected)
    {
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DetailedLog = detailed };
        var plan = BuildReady(settings).Settings.Read;
        Assert.Equal(expected, plan.OtherEditorIds);
        Assert.Equal(expected, plan.OverriddenOthersList);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void TouchChainsAreShownForTheDetailedLogOrTheReportFiles(bool detailed, bool reports, bool expected)
    {
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DetailedLog = detailed, WriteReportFiles = reports };

        Assert.Equal(expected, BuildReady(settings).Settings.AlsoRemove.TouchChainsShown);
    }

    [Theory]
    [InlineData(nameof(NpcHandling.Never))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInsideTheObject))]
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
        supportOnly.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.OnlyWhatLosesItsSupport };
        Assert.Equal(new WhatToCollect(true, true, false, false, false, false, false), BuildReady(supportOnly).Settings.Read);

        var relocating = SettingsFor();
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
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouchingIt };
        Assert.Equal(new WhatToCollect(false, false, false, false, false, false, false), BuildReady(neither).Settings.Read);
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
    public void TheChangedTerrainSettingsAreOffByDefaultAndPassedThrough()
    {
        var defaults = BuildReady(SettingsFor()).Settings.ChangedTerrain;
        Assert.Equal(
            new ChangedTerrainSettings(
                On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: true, HeightTolerance: 16f, CountTerrainTypeChanges: true,
                MinTerrainTypeChangePercent: 20, AreaBySize: false, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 64f),
            defaults);

        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes,
            CountHeightChanges = false,
            HeightChangeTolerance = 0f,
            MinTerrainTypeChangePercent = 50,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 2f },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 128f },
        };
        var ready = BuildReady(settings);

        Assert.Empty(ready.Warnings);
        Assert.Equal(
            new ChangedTerrainSettings(
                On: true, TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, false, 0f, true, 50,
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
        settings.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = true, CountHeightChanges = false, CountTerrainTypeChanges = false };

        var ready = BuildReady(settings);

        Assert.False(ready.Settings.ChangedTerrain.On);
        Assert.False(ready.Settings.Read.TerrainVersions);
        Assert.Equal(
            "Warning: changed terrain is on, but neither height nor terrain type changes are counted; no object is removed for changed terrain.",
            Assert.Single(ready.Warnings).Message);
    }

    [Fact]
    public void InvalidChangedTerrainValuesAreCorrectedEvenWhenTheStepIsOff()
    {
        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            TerrainCheckedObjects = (TerrainCheckedObjects)99,
            HeightChangeTolerance = 300f,
            BySize = new TerrainBySizeSettings { SizeMultiplier = float.NaN },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = -1f },
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            new ChangedTerrainSettings(
                On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, true, 256f, true, 20,
                AreaBySize: false, AreaMultiplier: 0.5f, AreaByDistance: true, AreaDistanceInUnits: 0f),
            ready.Settings.ChangedTerrain);
        string[] expectedStarts =
        [
            "Warning: objects checked for changed terrain",
            "Warning: height change tolerance 300",
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
    public void ARelativeReportFolderIsResolvedAgainstTheBaseFolder()
    {
        var warnings = new List<SettingWarning>();
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "Reports" };
        Assert.Equal(Path.Combine(ReportBaseFolder, "Reports"), SettingsValidator.ResolveReportFolder(ReportBaseFolder, StandardConfig.Of(settings), warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void TheDefaultReportFolderIsReportsInTheBaseFolder()
    {
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "" };
        Assert.Equal(Path.Combine(ReportBaseFolder, "Reports"), SettingsValidator.ResolveReportFolder(ReportBaseFolder, StandardConfig.Of(settings), []));
    }

    [Fact]
    public void AConfigFilesReportsGoToASubfolderNamedAfterTheConfig()
    {
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "Reports" };

        var ready = Assert.IsType<RunRequest.Ready>(Assert.Single(BuildBatch(settings).Requests));

        Assert.Equal(Path.Combine(ReportBaseFolder, "Reports", "town_walls"), ready.Settings.Reports.Folder);
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
        var failed = SettingsValidator.BuildAll(new ConfigsRead.Failed("Group file x is missing."), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1);

        Assert.Equal("Group file x is missing.", Assert.IsType<BatchRequest.Failed>(failed).Message);
    }

    [Fact]
    public void AConfigFileWithoutATargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), new StandardSettings());

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")} sets no mod to clean up (TargetPlugin). Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetOutsideTheLoadOrderFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Missing.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: the mod to clean up, Missing.esp, is not in the load order. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithThePatchPluginAsTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Patch.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: the mod to clean up, Patch.esp, is the patch this patcher writes. Nothing was run and no patch was written.",
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

        var failed = Assert.IsType<BatchRequest.Failed>(SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));

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

        var failed = Assert.IsType<BatchRequest.Failed>(SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));

        Assert.Equal(
            $"Config files {FilePathOf("first")} and {FilePathOf("second")} both clean up Target.esp; each mod may be cleaned up by one config only. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AStandardRequestThatStopsStaysAStopRequest()
    {
        var read = new ConfigsRead.Ready([StandardConfig.Of(new StandardSettings())]);

        var batch = Assert.IsType<BatchRequest.Ready>(SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));

        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), Assert.IsType<RunRequest.Stop>(Assert.Single(batch.Requests)).Reason);
    }

    private static string FilePathOf(string name) => Path.Combine("configs", name + ".json");

    private static SettingsSource.ConfigFile ConfigSource(string name, int number, int count) =>
        new(name, name + ".json", FilePathOf(name), number, count, new ConfigFileNotes([], [], []));

    private static BatchRequest.Ready BuildBatch(StandardSettings settings) => BuildBatch(ConfigSource("town_walls", 1, 1), settings);

    private static BatchRequest.Ready BuildBatch(SettingsSource source, StandardSettings settings) =>
        Assert.IsType<BatchRequest.Ready>(
            SettingsValidator.BuildAll(
                new ConfigsRead.Ready([new ConfiguredSettings(source, settings)]), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));

    private static BatchRequest.Failed FailedBatch(SettingsSource source, StandardSettings settings) =>
        Assert.IsType<BatchRequest.Failed>(
            SettingsValidator.BuildAll(
                new ConfigsRead.Ready([new ConfiguredSettings(source, settings)]), new PluginNameParsingPlugin(), Facts(), ReportBaseFolder, 1));

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
