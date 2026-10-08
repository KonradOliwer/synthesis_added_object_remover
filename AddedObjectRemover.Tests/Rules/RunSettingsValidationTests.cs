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
        Assert.IsType<RunRequest.Ready>(RunSettingsValidation.Build(settings, new PluginNameParsingPlugin(), facts ?? Facts(), OutputPath, workers));

    [Theory]
    [InlineData(null, (int)StopKind.NoTargetSet, null)]
    [InlineData("  ", (int)StopKind.NoTargetSet, null)]
    [InlineData("NoExtension", (int)StopKind.InvalidTargetName, "NoExtension")]
    [InlineData("Missing.esp", (int)StopKind.TargetNotInLoadOrder, "Missing.esp")]
    public void ATargetThatCannotBeUsedStopsTheRun(string? target, int kind, string? name)
    {
        var stop = Assert.IsType<RunRequest.Stop>(RunSettingsValidation.Build(SettingsFor(target), new PluginNameParsingPlugin(), Facts(), OutputPath, 1));
        Assert.Equal(new StopReason((StopKind)kind, name), stop.Reason);
    }

    [Fact]
    public void ValidDefaultSettingsGiveNoWarnings()
    {
        var ready = BuildReady(SettingsFor());
        Assert.Empty(ready.Warnings);
        Assert.Equal(Target, ready.Options.Target);
        Assert.Equal(3, ready.Options.Execution.Workers);
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
        settings.WhatToCheck.SizeMultiplier = 9f;
        settings.WhatToCheck.ZoneShape = (ZoneShape)99;
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
        Assert.Equal("Other.esp", Assert.Single(BuildReady(settings).Options.ExcludedNames));
    }

    [Fact]
    public void TheTargetIsComparedAsTheLoadOrderSpellsItAndPrintedAsTheSettingsSpellIt()
    {
        var ready = BuildReady(SettingsFor(" TARGET.ESP "));
        Assert.Equal("Target.esp", ready.Options.Target.FileName);
        Assert.Equal("Target.esp", ready.Options.ModIdentification.Target.FileName);
        Assert.Equal("TARGET.esp", ready.Options.TargetAsTyped.FileName);
    }

    [Fact]
    public void KnownExcludedPluginsAreSpelledAsTheLoadOrderListsThem()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = ["PATCH.ESP", "Unknown.esp"] };
        Assert.Equal("Patch.esp", Assert.Single(BuildReady(settings).Options.ModIdentification.Excluded).FileName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EditorIdAndOverriddenListsFollowTheDetailedLog(bool detailed, bool expected)
    {
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DetailedLog = detailed };
        var plan = BuildReady(settings).Options.Read;
        Assert.Equal(expected, plan.OtherEditorIds);
        Assert.Equal(expected, plan.OverriddenOthersList);
    }

    [Fact]
    public void SurroundingsAreReadForSupportModeAndNavmeshesOnlyForMarkerMoves()
    {
        var supportOnly = SettingsFor();
        supportOnly.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.ObjectsSupportedByIt };
        Assert.Equal(new WhatToCollect(true, true, false, false, false), BuildReady(supportOnly).Options.Read);

        var relocating = SettingsFor();
        relocating.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouching };
        relocating.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = true,
            MoveKeptMarkersOutOfOtherModsObjects = true,
        };
        var options = BuildReady(relocating).Options;
        Assert.Equal(new WhatToCollect(true, true, true, false, false), options.Read);
        Assert.NotNull(options.MarkerMoves);

        var neither = SettingsFor();
        neither.FollowUpRemoval = new FollowUpRemovalSettings { Mode = FollowUpRemovalMode.EverythingTouching };
        Assert.Equal(new WhatToCollect(false, false, false, false, false), BuildReady(neither).Options.Read);
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
        var options = BuildReady(settings).Options;
        Assert.Null(options.MarkerMoves);
        Assert.Null(options.LeftBehind);
    }

    [Fact]
    public void AnchoringThresholdBecomesAFraction()
    {
        var settings = SettingsFor();
        settings.FollowUpRemoval = new FollowUpRemovalSettings { AnchoringThresholdPercent = 25f };
        Assert.Equal(0.25f, BuildReady(settings).Options.AlsoRemove.SupportLostFraction);
    }

    [Fact]
    public void ARelativeReportFolderIsResolvedAgainstTheOutputFolder()
    {
        var warnings = new List<SettingWarning>();
        var settings = SettingsFor();
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "Reports" };
        Assert.Equal(Path.Combine(Path.GetDirectoryName(OutputPath)!, "Reports"), RunSettingsValidation.ResolveReportFolder(OutputPath, settings, warnings));
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
