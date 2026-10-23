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

    private const string RoughBoxesName = "Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster)";

    private static readonly SettingInFile Defaulted = new("TooClose.UseRoughBoxes", RoughBoxesName, "false");
    private static readonly SettingInFile DefaultedList = new("Trust.TrustedModsAndTheirMasters", "Trusted mods, and their masters", "[]");
    private static readonly SettingInFile Changed = new("TooClose.UseRoughBoxes", RoughBoxesName, "true");

    [Fact]
    public void TheDelimiterNamesTheConfigItsPlaceInTheGroupAndItsTarget()
    {
        var sections = StartSections(Notes([], [], []), detailedLog: false);

        Assert.Equal([$"=== Config 2 of 3: {FileName}, mod to clean up: {Target.FileName} ==="], Lines(sections, "configDelimiter"));
    }

    [Fact]
    public void TheDelimiterComesFirstAndTheWarningsFollowTheConfigFileLines()
    {
        var sections = StartSections(Notes(["Colour"], [Defaulted], [Changed]), detailedLog: true);

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
    public void SettingsTheFileLeavesOutAreListedOnOneLineWithTheDefaultValueUsed()
    {
        var sections = StartSections(Notes([], [Defaulted, DefaultedList], []), detailedLog: false);

        Assert.Equal(
            [$"Not set in {FileName}, defaults used: TooClose.UseRoughBoxes = false, Trust.TrustedModsAndTheirMasters = []"],
            Lines(sections, "configDefaulted"));
    }

    [Fact]
    public void ValuesThatDifferFromTheDefaultAreListedOnlyWithTheDetailedLog()
    {
        var notes = Notes([], [], [Changed]);

        Assert.Empty(Lines(StartSections(notes, detailedLog: false), "configNonDefault"));
        Assert.Equal(
            [$"Set in {FileName} to other than the default:", $"  {RoughBoxesName} (TooClose.UseRoughBoxes): true"],
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
            StandardConfig.Of(new StandardSettings { CleanedMod = Target }), new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1);

        Assert.Equal(["warnings"], LogAndReports.BuildStartSections(request).Select(section => section.Id));
    }

    /// <summary>Two runs of a batch: the first starts from a patch earlier patchers already wrote into, the second from its own mod's objects only.</summary>
    [Fact]
    public void EachRunsSummaryGivesItsStartingStateAndWhatItRemoved()
    {
        Assert.Equal(
            ["Objects of Target.esp: 9; already removed by earlier patchers in this group: 2; judged in this run: 6; removed in this run: 4."],
            RunSummaryLines(Target, objects: 9, removedByEarlierPatchers: 2, judged: 6, removed: 4));
        Assert.Equal(
            ["Objects of Second.esp: 3; already removed by earlier patchers in this group: 0; judged in this run: 3; removed in this run: 0."],
            RunSummaryLines(ModKey.FromNameAndExtension("Second.esp"), objects: 3, removedByEarlierPatchers: 0, judged: 3, removed: 0));
    }

    private static string[] RunSummaryLines(ModKey cleanedMod, int objects, int removedByEarlierPatchers, int judged, int removed)
    {
        var world = TestScenes.CreateWorld(TestTargets.CreateMany(judged), []);
        var counts = world.Counts with { CleanedModObjects = objects, RemovedByEarlierPatchers = removedByEarlierPatchers };
        return [.. LogSections.BuildRunSummarySection(world with { Counts = counts }, cleanedMod.ToPluginName(), removed).Lines];
    }

    private static ConfigFileNotes Notes(
        ImmutableArray<string> unknownKeys, ImmutableArray<SettingInFile> notInFile, ImmutableArray<SettingInFile> changedFromDefault) =>
        new(unknownKeys, notInFile, changedFromDefault);

    private static ImmutableArray<LogSection> StartSections(ConfigFileNotes notes, bool detailedLog)
    {
        var source = new SettingsSource.ConfigFile("town_walls", FileName, Path.Combine("configs", FileName), Number: 2, Count: 3, notes);
        var settings = new StandardSettings { CleanedMod = Target };
        var request = SettingsValidator.Build(new ConfiguredSettings(source, settings), new PluginNameParsingPlugin(), Facts(), detailedLog, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1);
        return LogAndReports.BuildStartSections(request);
    }

    private static string[] Lines(ImmutableArray<LogSection> sections, string id) => [.. sections.Single(section => section.Id == id).Lines];

    private static LoadOrderPlugins Facts()
    {
        var table = new KnownPluginNames(new[] { Skyrim, Target, Patch }.Select(key => key.ToPluginName()));
        PluginListing Listing(ModKey key, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]);
        return TestLoadOrderPlugins.Of(table, Patch.ToPluginName(), [Listing(Skyrim), Listing(Target, Skyrim), Listing(Patch, Skyrim)]);
    }
}
