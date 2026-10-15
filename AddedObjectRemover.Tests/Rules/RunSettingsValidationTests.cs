using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class RunSettingsValidationTests
{
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private const string OutputPath = "/out/Patch.esp";

    private static LoadOrderPlugins Facts(bool targetLoaded = true) =>
        new(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, targetLoaded, []), new PluginListing(Patch, true, [])]);

    private static Settings SettingsFor(string? target = "Target.esp") =>
        new() { WhatToCheck = new CheckSettings { TargetPlugin = target! } };

    private static RunRequest.Ready BuildReady(Settings settings, LoadOrderPlugins? facts = null, int workers = 3) =>
        Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(settings, new PluginNameParsingPlugin(), facts ?? Facts(), OutputPath, workers));

    [Theory]
    [InlineData(null, (int)StopKind.NoTargetSet, null)]
    [InlineData("  ", (int)StopKind.NoTargetSet, null)]
    [InlineData("NoExtension", (int)StopKind.InvalidTargetName, "NoExtension")]
    [InlineData("Missing.esp", (int)StopKind.TargetNotInLoadOrder, "Missing.esp")]
    public void ATargetThatCannotBeUsedStopsTheRun(string? target, int kind, string? name)
    {
        var stop = Assert.IsType<RunRequest.Stop>(SettingsValidator.Build(SettingsFor(target), new PluginNameParsingPlugin(), Facts(), OutputPath, 1));
        Assert.Equal(new StopReason((StopKind)kind, name), stop.Reason);
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
            ExcludedPlugins = ["bad"],
            MaxOtherMastersForPatch = 500,
            NpcHandling = (NpcHandling)99,
        };
        settings.TooClose.SizeMultiplier = 9f;
        settings.TooClose.ZoneShape = (ZoneShape)99;
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = (FollowUpRemovalMode)99,
            TouchDistance = 100f,
            AnchoringThresholdPercent = 500f,
        };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            ProtectedTypes = (ProtectedInvisibleObjectsPreset)99,
            SearchRadius = 1f,
            DirectionThresholdPercent = 45,
            RemovedDirectionsPercent = 44,
            OccupiedDirectionsPercent = 5,
            CustomProtectedTypes = [(InvisibleObjectKind)99],
        };
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "bad\0folder" };

        var messages = BuildReady(settings, Facts(targetLoaded: false)).Warnings.Select(warning => warning.Message).ToList();

        string[] expectedStarts =
        [
            "Warning: excluded plugin 'bad'",
            "Warning: could not read Target.esp",
            "Warning: maximum other masters for a patch",
            "Warning: NPCs and creatures setting",
            "Warning: size multiplier",
            "Warning: removal zone",
            "Warning: follow-up removal mode",
            "Warning: touch distance",
            "Warning: anchoring threshold",
            "Warning: protected types",
            "Warning: search radius",
            "Warning: removed area per direction 45 is not a multiple",
            "Warning: removed directions required 44 is not a multiple",
            "Warning: occupied directions required 5 is outside",
            "Warning: custom protected type",
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
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = [" Other.esp ", "bad", ""] };
        Assert.Equal("Other.esp", Assert.Single(BuildReady(settings).Settings.ExcludedNames));
    }

    [Fact]
    public void TheTargetIsComparedAsTheLoadOrderSpellsItAndPrintedAsTheSettingsSpellIt()
    {
        var ready = BuildReady(SettingsFor(" TARGET.ESP "));
        Assert.Equal("Target.esp", ready.Settings.Target.FileName);
        Assert.Equal("Target.esp", ready.Settings.ModIdentification.Target.FileName);
        Assert.Equal("TARGET.esp", ready.Settings.TargetAsTyped.FileName);
    }

    [Fact]
    public void KnownExcludedPluginsAreSpelledAsTheLoadOrderListsThem()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = ["PATCH.ESP", "Unknown.esp"] };
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
    [InlineData(nameof(NpcHandling.Ignore))]
    [InlineData(nameof(NpcHandling.OnlyWhenStuckInObject))]
    [InlineData(nameof(NpcHandling.CountLikeObjects))]
    public void TheSelectStepHandlesNpcsAsTheTooCloseStepDoes(string handling)
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { NpcHandling = Enum.Parse<NpcHandling>(handling) };

        var ready = BuildReady(settings);

        Assert.Equal(Enum.Parse<NpcHandling>(handling), ready.Settings.Selection.Npcs);
        Assert.Equal(ready.Settings.TooClose.Npcs, ready.Settings.Selection.Npcs);
    }

    [Fact]
    public void AnInvalidNpcSettingGivesBothStepsTheDefault()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { NpcHandling = (NpcHandling)99 };

        var ready = BuildReady(settings);

        Assert.Equal(SettingDefaults.Npcs, ready.Settings.Selection.Npcs);
        Assert.Equal(SettingDefaults.Npcs, ready.Settings.TooClose.Npcs);
    }

    [Fact]
    public void SurroundingsAreReadForSupportModeAndNavmeshesOnlyForMarkerMoves()
    {
        var supportOnly = SettingsFor();
        supportOnly.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.ObjectsSupportedByIt };
        Assert.Equal(new WhatToCollect(true, true, false, false, false, false, false), BuildReady(supportOnly).Settings.Read);

        var relocating = SettingsFor();
        relocating.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouching };
        relocating.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = true,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        var runSettings = BuildReady(relocating).Settings;
        Assert.Equal(new WhatToCollect(true, true, true, false, false, false, false), runSettings.Read);
        Assert.NotNull(runSettings.MarkerMoves);

        var neither = SettingsFor();
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouching };
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
                TerrainDistanceMode.GameUnits, DistanceInUnits: 64f, DistanceSizeMultiplier: 0.5f),
            defaults);

        var settings = SettingsFor();
        settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjects,
            CountHeightChanges = false,
            HeightChangeTolerance = 0f,
            TerrainDistanceMode = TerrainDistanceMode.ObjectSize,
            TerrainDistanceInUnits = 128f,
            TerrainDistanceSizeMultiplier = 2f,
        };
        var ready = BuildReady(settings);

        Assert.Empty(ready.Warnings);
        Assert.Equal(
            new ChangedTerrainSettings(On: true, TerrainCheckedObjects.AllObjects, false, 0f, true, TerrainDistanceMode.ObjectSize, 128f, 2f),
            ready.Settings.ChangedTerrain);
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
            TerrainDistanceMode = (TerrainDistanceMode)99,
            TerrainDistanceInUnits = -1f,
            TerrainDistanceSizeMultiplier = float.NaN,
        };

        var ready = BuildReady(settings);

        Assert.Equal(
            new ChangedTerrainSettings(
                On: false, TerrainCheckedObjects.VisibleObjectsOnTheGround, true, 256f, true, TerrainDistanceMode.GameUnits, 0f, 0.5f),
            ready.Settings.ChangedTerrain);
        string[] expectedStarts =
        [
            "Warning: objects checked for changed terrain",
            "Warning: height change tolerance 300",
            "Warning: terrain distance mode",
            "Warning: terrain distance in game units -1",
            "Warning: terrain distance size multiplier is not a valid number",
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
    public void ARelativeReportFolderIsResolvedAgainstTheOutputFolder()
    {
        var warnings = new List<SettingWarning>();
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "Reports" };
        Assert.Equal(Path.Combine(Path.GetDirectoryName(OutputPath)!, "Reports"), SettingsValidator.ResolveReportFolder(OutputPath, settings, warnings));
        Assert.Empty(warnings);
    }

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
