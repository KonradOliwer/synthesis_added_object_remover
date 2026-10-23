using System.Collections.Immutable;
using System.Reflection;
using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.IdentifyTheMods;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Settings;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// A mod that requires the cleaned mod and loads after the patch overrides the patch's records for what it overrides: the run
/// stops, naming the one that loads last, or with the switch on every such mod is a warning and the run goes on.
/// </summary>
public class RequiringModAfterPatchTests
{
    private const string ReportsFolder = "/out";
    private const string SwitchName = "Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch";
    private const string LateWarning =
        "Warning: Late.esp requires Target.esp and loads after the patch Patch.esp (position 4 > 3); the objects of Target.esp that it overrides are left unchanged.";
    private const string LaterWarning =
        "Warning: Later.esp requires Target.esp and loads after the patch Patch.esp (position 6 > 3); the objects of Target.esp that it overrides are left unchanged.";

    private static readonly PluginName Base = new("Skyrim.esm");
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private static readonly PluginName Early = new("Early.esp");
    private static readonly PluginName Late = new("Late.esp");
    private static readonly PluginName Unrelated = new("Unrelated.esp");
    private static readonly PluginName Later = new("Later.esp");

    /// <summary>The patch is at position 3; Early is above it, the others below.</summary>
    private static readonly PluginName[] PluginList = [Base, Target, Early, Patch, Late, Unrelated, Later];

    [Fact]
    public void TheRequiringModThatLoadsLastBelowThePatchStopsTheRunWithBothPositions()
    {
        var stop = Assert.IsType<RunRequest.Stop>(Build(Facts(PluginList, Early, Late, Later), onlyWarn: false));

        Assert.Equal(new StopReason(StopKind.RequiringModLoadsAfterPatch, "Target.esp", new PatchPlacement("Patch.esp", "Later.esp", 6, 3)), stop.Reason);
    }

    [Fact]
    public void ThePatchThePluginsAboveItAndThePluginsThatDoNotRequireTheCleanedModAreNotReported()
    {
        var ready = Assert.IsType<RunRequest.Ready>(Build(Facts(PluginList, Early), onlyWarn: false));

        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void WithTheSwitchOnEachRequiringModBelowThePatchIsAWarningInPluginListOrder()
    {
        var ready = Assert.IsType<RunRequest.Ready>(Build(Facts(PluginList, Early, Later, Late), onlyWarn: true));

        Assert.Equal([LateWarning, LaterWarning], ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheSwitchGivesNoWarningWhenNoRequiringModLoadsBelowThePatch()
    {
        var ready = Assert.IsType<RunRequest.Ready>(Build(Facts(PluginList, Early), onlyWarn: true));

        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void AConfigFileWithARequiringModBelowThePatchFailsTheBatchNamingTheFileTheModAndBothPositions()
    {
        var failed = Assert.IsType<BatchRequest.Failed>(BuildBatch(Facts(PluginList, Late, Later), onlyWarn: false));

        Assert.Equal(
            $"Config file {FilePath}: Later.esp requires Target.esp and loads after the patch Patch.esp (position 6 > 3): move Patch.esp below it in your plugin list, or turn on \"{SwitchName}\". Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithARequiringModBelowThePatchRunsWithAWarningWhenTheSwitchIsOn()
    {
        var batch = Assert.IsType<BatchRequest.Ready>(BuildBatch(Facts(PluginList, Late), onlyWarn: true));

        var ready = Assert.IsType<RunRequest.Ready>(Assert.Single(batch.Requests));
        Assert.Equal([LateWarning], ready.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void TheStandardStopNamesTheModBothPositionsThePatchAndTheSwitch()
    {
        var reason = new StopReason(StopKind.RequiringModLoadsAfterPatch, "Target.esp", new PatchPlacement("Patch.esp", "Later.esp", 6, 3));

        Assert.Equal(
            [$"Later.esp requires Target.esp and loads after the patch Patch.esp (position 6 > 3): move Patch.esp below it in your plugin list, or turn on \"{SwitchName}\". No changes made."],
            LogSections.BuildStopSection(reason, new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void TheSwitchNameInTheTextsIsTheNameTheSettingsScreenShows()
    {
        var property = typeof(Settings).GetProperty(nameof(Settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch))!;
        var ready = Assert.IsType<RunRequest.Ready>(Build(Facts(PluginList, Late), onlyWarn: true));

        Assert.Equal(SwitchName, property.GetCustomAttribute<SynthesisSettingName>()!.Name);
        Assert.Equal(SwitchName, Assert.Single(ready.Warnings).Setting);
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public void TheStartSectionEchoesTheSwitch(bool onlyWarn, string shown)
    {
        var facts = Facts(PluginList, Early);
        var settings = Assert.IsType<RunRequest.Ready>(Build(facts, onlyWarn)).Settings;

        var lines = LogSections.BuildConfigSection(settings, ModTrustClassifier.Classify(facts, settings.ModTrust), facts).Lines;

        Assert.Contains($"{SwitchName}: {shown}.", lines);
    }

    [Fact]
    public void TheSwitchIsOffUntilTheUserTurnsItOn()
    {
        Assert.False(new Settings().OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch);
        Assert.True(((IModeSettingsValues)new Settings { OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch = true }).OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch);
    }

    private static string FilePath => Path.Combine("configs", "town_walls.json");

    /// <summary>The patch lists the cleaned mod as a master too, as a patch that already holds an override does: it is never a requiring mod.</summary>
    private static ImmutableArray<PluginName> MastersOf(PluginName plugin, PluginName[] requiring)
    {
        if (plugin == Base) return [];
        if (plugin == Target) return [Base];
        return plugin == Patch || requiring.Contains(plugin) ? [Base, Target] : [Base];
    }

    private static LoadOrderPlugins Facts(PluginName[] pluginList, params PluginName[] requiring) =>
        TestLoadOrderPlugins.Of(
            new KnownPluginNames(pluginList),
            Patch,
            [.. pluginList.Select(plugin => new PluginListing(plugin, true, MastersOf(plugin, requiring)))],
            pluginList,
            []);

    private static StandardSettings SettingsForTarget() => new() { CleanedMod = ModKey.FromNameAndExtension(Target.ToString()) };

    private static RunRequest Build(LoadOrderPlugins facts, bool onlyWarn) =>
        SettingsValidator.Build(
            StandardConfig.Of(SettingsForTarget()),
            new PluginNameParsingPlugin(),
            facts,
            detailedLogAndReports: false,
            onlyWarnWhenRequiringModLoadsAfterPatch: onlyWarn,
            ReportsFolder,
            1);

    private static BatchRequest BuildBatch(LoadOrderPlugins facts, bool onlyWarn)
    {
        var source = new SettingsSource.ConfigFile("town_walls", "town_walls.json", FilePath, 1, 1, new ConfigFileNotes([], [], []));
        var read = new ConfigsRead.Ready([new ConfiguredSettings(source, SettingsForTarget())]);
        return SettingsValidator.BuildAll(
            read, new PluginNameParsingPlugin(), facts, detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: onlyWarn, ReportsFolder, 1);
    }
}
