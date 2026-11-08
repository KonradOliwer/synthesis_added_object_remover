using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>The load orders and settings the tests of the run settings validation build their requests from.</summary>
internal static class RunSettingsValidationScene
{
    public static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    public const string ReportsFolder = "/out";

    public static LoadOrderPlugins Facts(bool targetLoaded = true) =>
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
    public static readonly LoadOrderPlugins DisabledTargetFacts = FactsWithPluginList([Base, Target, Patch], Target);
    public static readonly LoadOrderPlugins NotLoadedTargetFacts = FactsWithPluginList([Base, Target, Patch]);
    public static readonly LoadOrderPlugins TargetAfterPatchFacts = FactsWithPluginList([Base, Patch, Middle, Target]);

    public static StandardSettings SettingsFor(string target = "Target.esp") =>
        new() { CleanedMod = ModKey.FromNameAndExtension(target) };

    /// <summary>For the tests that ask what the standard settings alone read: the changed objects check no terrain and no old place.</summary>
    public static StandardSettings WithoutChangedObjectsTerrainAndOldPlaces(StandardSettings settings)
    {
        settings.ChangedObjects.Collision.TerrainChange.RemoveOnChangedTerrain = false;
        settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
        return settings;
    }

    public static RunRequest Build(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, workers);

    public static RunRequest.Ready BuildReady(StandardSettings settings, LoadOrderPlugins? facts = null, bool detailedLogAndReports = false, int workers = 3) =>
        Assert.IsType<RunRequest.Ready>(Build(settings, facts, detailedLogAndReports, workers));

    public static BatchRequest BuildAll(ConfigsRead read, LoadOrderPlugins? facts = null) =>
        BatchRequests.BuildAll(read, new PluginNameParsingPlugin(), facts ?? Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1);

    public static RestoreWhereRemovedSettings AllRestoresOff() =>
        new() { RestoreOtherModsObjects = false, RestoreTerrain = false, RestoreCells = false, RestoreNavmeshes = false };
}
