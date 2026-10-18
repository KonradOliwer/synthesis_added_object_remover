using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The lines that open one config's run in a batch log: the delimiter, then what the config file left out, got wrong or changed.</summary>
public sealed class BatchLogSectionsTests
{
    private const string ReportsFolder = "/out";
    private const string FileName = "town_walls.json";

    private static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    private static readonly ModKey Target = TestTargets.TargetMod;
    private static readonly ModKey Patch = ModKey.FromNameAndExtension("Patch.esp");

    private static readonly SettingInFile WholeGroup = new("LeftoverInvisibleObjects", "Invisible objects left behind", true, null);
    private static readonly SettingInFile SingleSetting =
        new("TooClose.UseRoughBoxes", "Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster)", false, null);
    private static readonly SettingInFile Changed =
        new("TooClose.UseRoughBoxes", "Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster)", false, "true");

    [Fact]
    public void TheDelimiterNamesTheConfigItsPlaceInTheGroupAndItsTarget()
    {
        var sections = StartSections(Notes([], [], []), detailedLog: false);

        Assert.Equal([$"=== Config 2 of 3: {FileName}, mod to clean up: {Target.FileName} ==="], Lines(sections, "configDelimiter"));
    }

    [Fact]
    public void TheDelimiterComesFirstAndTheWarningsFollowTheConfigFileLines()
    {
        var sections = StartSections(Notes(["Colour"], [WholeGroup], [Changed]), detailedLog: true);

        Assert.Equal(
            ["configDelimiter", "configIgnoredKeys", "configDefaulted", "configNonDefault", "warnings"],
            sections.Select(section => section.Id));
    }

    [Fact]
    public void KeysThePatcherDoesNotKnowAreListedAsIgnored()
    {
        var sections = StartSections(Notes(["TooClose.UseRoughBox", "Colour"], [], []), detailedLog: false);

        Assert.Equal(
            [$"Ignored in {FileName}, because this patcher has no such setting (check the spelling and the section it is in): TooClose.UseRoughBox, Colour."],
            Lines(sections, "configIgnoredKeys"));
    }

    [Fact]
    public void SettingsTheFileLeavesOutAreListedWithTheDefaultBeingUsed()
    {
        var sections = StartSections(Notes([], [SingleSetting, WholeGroup], []), detailedLog: false);

        Assert.Equal(
            [
                $"Not in {FileName}, so the default is used:",
                "  Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster) (TooClose.UseRoughBoxes)",
                "  Invisible objects left behind (LeftoverInvisibleObjects), all settings in it",
            ],
            Lines(sections, "configDefaulted"));
    }

    [Fact]
    public void ValuesThatDifferFromTheDefaultAreListedOnlyWithTheDetailedLog()
    {
        var notes = Notes([], [], [Changed]);

        Assert.Empty(Lines(StartSections(notes, detailedLog: false), "configNonDefault"));
        Assert.Equal(
            [$"Set in {FileName} to other than the default:", "  Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster) (TooClose.UseRoughBoxes): true"],
            Lines(StartSections(notes, detailedLog: true), "configNonDefault"));
    }

    [Fact]
    public void ALineListPrintsOnlyWhenItHasAnEntry()
    {
        var sections = StartSections(Notes([], [], []), detailedLog: true);

        Assert.Empty(Lines(sections, "configIgnoredKeys"));
        Assert.Empty(Lines(sections, "configDefaulted"));
        Assert.Empty(Lines(sections, "configNonDefault"));
    }

    [Fact]
    public void AStandardRunHasNoConfigFileLines()
    {
        var request = SettingsValidator.Build(
            StandardConfig.Of(new StandardSettings { TargetPlugin = Target }), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: false, ReportsFolder, 1);

        Assert.Equal(["warnings"], LogAndReports.BuildStartSections(request).Select(section => section.Id));
    }

    [Fact]
    public void TheScanSaysWhatEarlierConfigsRemovedAndMovedOnlyWhenThereWasSomething()
    {
        Assert.Equal(["Scanned 0 placed records: 0 Target.esp objects to check, 0 objects from other mods."], ScanLines(removed: 0, moved: 0));
        Assert.Equal(
            [
                "Scanned 0 placed records: 0 Target.esp objects to check, 0 objects from other mods.",
                "  Ignored 37 objects that earlier configs of this batch removed.",
                "  2 objects that earlier configs of this batch moved are checked at their new position.",
            ],
            ScanLines(removed: 37, moved: 2));
    }

    private static string[] ScanLines(int removed, int moved)
    {
        var world = TestScenes.CreateWorld([], []);
        var counts = world.Counts with { RemovedByEarlierRuns = removed, MovedByEarlierRuns = moved };
        return [.. LogSections.BuildScanSection(world with { Counts = counts }, Target.ToPluginName(), TimeSpan.Zero, new ReportContext(null!, null!, Detailed: false)).Lines];
    }

    private static ConfigFileNotes Notes(
        ImmutableArray<string> unknownKeys, ImmutableArray<SettingInFile> notInFile, ImmutableArray<SettingInFile> changedFromDefault) =>
        new(unknownKeys, notInFile, changedFromDefault);

    private static ImmutableArray<LogSection> StartSections(ConfigFileNotes notes, bool detailedLog)
    {
        var source = new SettingsSource.ConfigFile("town_walls", FileName, Path.Combine("configs", FileName), Number: 2, Count: 3, notes);
        var settings = new StandardSettings { TargetPlugin = Target };
        var request = SettingsValidator.Build(new ConfiguredSettings(source, settings), new PluginNameParsingPlugin(), Facts(), detailedLog, ReportsFolder, 1);
        return LogAndReports.BuildStartSections(request);
    }

    private static string[] Lines(ImmutableArray<LogSection> sections, string id) => [.. sections.Single(section => section.Id == id).Lines];

    private static LoadOrderPlugins Facts()
    {
        var table = new KnownPluginNames(new[] { Skyrim, Target, Patch }.Select(key => key.ToPluginName()));
        PluginListing Listing(ModKey key, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]);
        return new LoadOrderPlugins(table, Patch.ToPluginName(), [Listing(Skyrim), Listing(Target, Skyrim), Listing(Patch, Skyrim)]);
    }
}
