using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

using static LogSectionScene;

/// <summary>The config log section gives the text checked in as expected-output files with the detailed log on, and only what a mod user needs with it off.</summary>
[ExpectedOutputTest]
public sealed class StartSetupTooCloseSectionsTests
{
    private const string WaterChecksOn =
        "Count water height changes: on, ignoring changes up to 16 units; Count water added/removed changes: on; "
        + "remove only when this share of the ground got covered/uncovered: 20%.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Config_GivesTheTextWithAndWithoutPatchesAndLeftovers(bool everythingOn)
    {
        var settings = SettingsFor();
        settings.TooClose.ByDistance.Use = false;
        settings.TerrainChange.RemoveOnChangedTerrain = false;
        settings.Trust = new TrustSettings
        {
            TrustedModsAndTheirMasters = everythingOn ? [ModKey.FromNameAndExtension("Extra.esp"), ModKey.FromNameAndExtension("Second.esp")] : [],
            TrustModsRequiringCleanedModAndTheirMasters = everythingOn,
            MaxOtherMastersOfRequiringModToTrust = 1,
            TrustModsLoadingBeforeCleanedMod = everythingOn,
        };
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = everythingOn ? FollowUpRemovalMode.OnlyWhatLosesItsSupport : FollowUpRemovalMode.Nothing,
        };
        settings.LeftoverObjects = new LeftoverObjectSettings { RemoveBySurroundingDirections = everythingOn };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = everythingOn,
            MoveKeptMarkersOutOfOtherModsObjects = everythingOn,
        };
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods, detailedLogAndReports: everythingOn)).Settings;
        var trust = ModTrustClassifier.Classify(new(mods, runSettings.ModTrust));

        var lines = LogSections.BuildConfigSection(runSettings, trust, mods).Lines.Select(line => line.Replace('\\', '/')).ToArray();

        AssertExpectedOutput(lines, "Config_GivesTheTextWithAndWithoutPatchesAndLeftovers", $"{everythingOn}");
    }

    [Theory]
    [InlineData(true, 1f, 5f, "Objects left behind, area method: radius 5000 units; cleared when at most 1% by size and 5% by number still stand; kept with fewer than 5 of the cleaned mod's other objects around.")]
    [InlineData(true, 0.5f, 2.5f, "Objects left behind, area method: radius 5000 units; cleared when at most 0.5% by size and 2.5% by number still stand; kept with fewer than 5 of the cleaned mod's other objects around.")]
    [InlineData(false, 1f, 5f, "Objects left behind, area method: off.")]
    [InlineData(true, 1f, 100f, "Objects left behind, area method: off (the count limit is 100% or more).")]
    [InlineData(true, 150f, 5f, "Objects left behind, area method: off (the size limit is 100% or more).")]
    [InlineData(true, 100f, 150f, "Objects left behind, area method: off (the size and count limits are 100% or more).")]
    public void Config_GivesTheAreaMethodLineWithItsLimitsOrWhyItIsOff(bool switchedOn, float sizeLimit, float countLimit, string expected)
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            RemoveObjectsInClearedAreas = switchedOn,
            ClearedArea = new LeftoverClearedAreaSettings { MaxStandingSizePercent = sizeLimit, MaxStandingCountPercent = countLimit },
        };
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.Equal(expected, lines.Single(line => line.StartsWith("Objects left behind, area method: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Config_GivesTheVisibleLeftBehindLineOffAndOnWithTheTickedKinds()
    {
        var settings = SettingsFor();
        settings.LeftoverObjects = new LeftoverObjectSettings { RemoveBySurroundingDirections = false };
        var mods = Facts();
        string VisibleLine()
        {
            var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;
            return LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines
                .Single(line => line.StartsWith("Objects left behind, direction method: ",StringComparison.Ordinal));
        }

        Assert.Equal("Objects left behind, direction method: off.",VisibleLine());

        settings.LeftoverObjects = new LeftoverObjectSettings
        {
            MinObjectsAround = 3,
            RemoveBySurroundingDirections = true,
            AlwaysKeep = new KeptVisibleObjectSettings { Containers = true, Furniture = true },
        };
        Assert.Equal(
            "Objects left behind, direction method: on; Look this far around it: 1024 units; Decide only if at least this many of the 8 directions have objects: 6; "
            + "A direction is cleared when this much of it was removed: 80%; Remove when this share of the directions with objects is cleared: 80%; "
            + "Never remove these as objects left behind: Containers, Furniture; kept with fewer than 3 of the cleaned mod's other objects around.",
            VisibleLine());
    }

    [Fact]
    public void Config_GivesTheRestoreWhereRemovedLineOffAndOnWithOnlyWhatIsOn()
    {
        var settings = SettingsFor();
        var mods = Facts();
        string RestoreLine()
        {
            var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;
            return LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines
                .Single(line => line.StartsWith("Restore where the cleaned mod was removed: ", StringComparison.Ordinal));
        }

        settings.RestoreWhereRemoved = new RestoreWhereRemovedSettings
        {
            RestoreOtherModsObjects = false, RestoreTerrain = false, RestoreCells = false, RestoreNavmeshes = false,
        };
        Assert.Equal("Restore where the cleaned mod was removed: off.", RestoreLine());

        settings.RestoreWhereRemoved = new RestoreWhereRemovedSettings();
        Assert.Equal(
            "Restore where the cleaned mod was removed: other mods' objects, terrain, cell settings, navmeshes; "
            + "an area 10000 units wide counts as cleared when at most 1% of the cleaned mod's objects there still stand by size and at most 5% by number; "
            + "terrain edges blended over 256 units.",
            RestoreLine());

        settings.RestoreWhereRemoved = new RestoreWhereRemovedSettings
        {
            RestoreOtherModsObjects = false,
            RestoreTerrain = false,
            ClearedArea = new ClearedAreaSettings { SearchRadius = 2048f, MaxStandingSizePercent = 2.5f, MaxStandingCountPercent = 10f },
        };
        Assert.Equal(
            "Restore where the cleaned mod was removed: cell settings, navmeshes; "
            + "an area 4096 units wide counts as cleared when at most 2.5% of the cleaned mod's objects there still stand by size and at most 10% by number.",
            RestoreLine());
    }

    [Theory]
    [InlineData(true, 80, 15, "Shrink or turn objects to make them fit: Shrink down to: 80% of its size; Turn by at most: 15 degrees either way.")]
    [InlineData(true, 100, 15, "Shrink or turn objects to make them fit: Shrink down to: 100% of its size; Turn by at most: 15 degrees either way.")]
    [InlineData(true, 80, 0, "Shrink or turn objects to make them fit: Shrink down to: 80% of its size; Turn by at most: 0 degrees either way.")]
    [InlineData(true, 100, 0, "Shrink or turn objects to make them fit: off.")]
    [InlineData(false, 80, 15, "Shrink or turn objects to make them fit: off.")]
    public void Config_GivesTheShrinkOrTurnLineOffWhenNothingCanBeShrunkOrTurned(bool switchOn, int shrink, int turn, string expected)
    {
        var settings = SettingsFor();
        settings.ShrinkOrTurn = new ShrinkOrTurnSettings { ShrinkOrTurnObjects = switchOn, ShrinkLimitPercent = shrink, TurnLimitDegrees = turn };
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;

        var line = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines
            .Single(line => line.StartsWith("Shrink or turn objects to make them fit", StringComparison.Ordinal));

        Assert.Equal(expected, line);
    }

    [Fact]
    public void Config_PrintsTheTrustSettingsAndOneLinePerTrustReasonInLoadOrder()
    {
        var settings = SettingsFor();
        settings.Trust = new TrustSettings { TrustedModsAndTheirMasters = [City], ModsTrustedForOwnChangesOnly = [Birds], NeverTrustedMods = [Never] };
        var mods = TrustFacts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.Equal(
            [
                "Which other mods can cause removals: Trusted mods, and their masters: City.esp; Mods trusted for their own changes only: Birds.esp; "
                    + "Never trusted mods: Never.esp; Also trust mods that require the cleaned mod, and their masters: on, only if it has at most 5 other masters; "
                    + "Trust mods loading before the cleaned mod: on.",
                "Trusted, base game: Skyrim.esm",
                "Trusted, masters of Target.esp: TargetMaster.esm",
                "Trusted, listed with masters: City.esp (masters: CityMaster.esm)",
                "Trusted, requires Target.esp: Compat.esp (masters: Other.esp)",
                "Trusted for own changes only, listed: Birds.esp",
                "Never trusted, listed: Never.esp",
                "Requires Target.esp but not trusted, 6 other masters (limit 5): Merged.esp",
                "Trusted, loading before Target.esp: 1 mod",
            ],
            lines.Skip(3).Take(9));
    }

    [Fact]
    public void Config_NamesTheMarkedPluginsRequiringTheCleanedModAndThoseOnlyLoadingBeforeItEachInLoadOrder()
    {
        var firstBefore = ModKey.FromNameAndExtension("First Before Patch.esp");
        var secondBefore = ModKey.FromNameAndExtension("Second Before Patch.esp");
        var second = ModKey.FromNameAndExtension("Second Group Patch.esp");
        var first = ModKey.FromNameAndExtension("First Group Patch.esp");
        PluginListing Listing(ModKey key, bool marked, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]) { CarriesPatchMarker = marked };
        PluginListing[] listed =
        [
            Listing(Skyrim, marked: false),
            Listing(secondBefore, marked: true, Skyrim),
            Listing(firstBefore, marked: true, Skyrim),
            Listing(Target, marked: false, Skyrim),
            Listing(second, marked: true, Skyrim, Target),
            Listing(first, marked: true, Skyrim, Target),
            Listing(Patch, marked: false, Skyrim),
        ];
        var mods = TestLoadOrderPlugins.Of(
            new KnownPluginNames(listed.SelectMany(listing => listing.Masters.Prepend(listing.Mod))), Patch.ToPluginName(), [.. listed]);
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(SettingsFor(), mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.Equal(
            [
                "Trusted, base game: Skyrim.esm",
                "Requires Target.esp but not trusted, Added Object Remover patch of another group: Second Group Patch.esp, First Group Patch.esp",
                "Loads before Target.esp but not trusted, Added Object Remover patch of another group: Second Before Patch.esp, First Before Patch.esp",
            ],
            lines.Skip(4).Take(3));
    }

    /// <summary>The patch marker decides before the other-master limit, so raising the limit would not change the line.</summary>
    [Fact]
    public void Config_NamesAMarkedPluginOverTheOtherMasterLimitAsAPatchOfAnotherGroup()
    {
        var otherGroup = ModKey.FromNameAndExtension("Other Group Patch.esp");
        ModKey[] extraMasters = [.. Enumerable.Range(1, 6).Select(number => ModKey.FromNameAndExtension($"Extra{number}.esp"))];
        PluginListing Listing(ModKey key, bool marked, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]) { CarriesPatchMarker = marked };
        PluginListing[] listed =
        [
            Listing(Skyrim, marked: false),
            Listing(Target, marked: false, Skyrim),
            .. extraMasters.Select(master => Listing(master, marked: false, Skyrim)),
            Listing(otherGroup, marked: true, [Skyrim, Target, .. extraMasters]),
            Listing(Patch, marked: false, Skyrim),
        ];
        var mods = TestLoadOrderPlugins.Of(
            new KnownPluginNames(listed.SelectMany(listing => listing.Masters.Prepend(listing.Mod))), Patch.ToPluginName(), [.. listed]);
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(SettingsFor(), mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.Contains("Requires Target.esp but not trusted, Added Object Remover patch of another group: Other Group Patch.esp", lines);
        Assert.DoesNotContain(lines, line => line.Contains("other masters (limit", StringComparison.Ordinal));
    }

    /// <summary>Such a patch is caught only as a master of a trusted mod that requires the cleaned mod, so it neither requires nor loads before it.</summary>
    [Fact]
    public void Config_NamesAMarkedPluginLoadingAfterTheCleanedModAsAMasterOfAModThatRequiresIt()
    {
        var otherGroup = ModKey.FromNameAndExtension("Other Group Patch.esp");
        var compat = ModKey.FromNameAndExtension("Compat.esp");
        PluginListing Listing(ModKey key, bool marked, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]) { CarriesPatchMarker = marked };
        PluginListing[] listed =
        [
            Listing(Skyrim, marked: false),
            Listing(Target, marked: false, Skyrim),
            Listing(otherGroup, marked: true, Skyrim),
            Listing(compat, marked: false, Skyrim, Target, otherGroup),
            Listing(Patch, marked: false, Skyrim),
        ];
        var mods = TestLoadOrderPlugins.Of(
            new KnownPluginNames(listed.SelectMany(listing => listing.Masters.Prepend(listing.Mod))), Patch.ToPluginName(), [.. listed]);
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(SettingsFor(), mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.Equal(
            [
                "Trusted, base game: Skyrim.esm",
                "Trusted, requires Target.esp: Compat.esp",
                "Master of a mod that requires Target.esp but not trusted, Added Object Remover patch of another group: Other Group Patch.esp",
            ],
            lines.Skip(4).Take(3));
    }

    [Fact]
    public void Config_PrintsNoLineForPatchesOfOtherGroupsWhenThereAreNone()
    {
        var mods = TrustFacts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(SettingsFor(), mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;

        Assert.DoesNotContain(lines, line => line.Contains("another group", StringComparison.Ordinal));
    }

    [Fact]
    public void Config_SaysOnlyThatChangedTerrainIsOffWhileItIsOff()
    {
        var lines = ConfigLines(settings => settings.TerrainChange.RemoveOnChangedTerrain = false);

        Assert.Contains("Remove objects on terrain changed by other mods: off.", lines);
    }

    [Fact]
    public void Config_GivesTheChangedTerrainChecksThatAreOn()
    {
        var lines = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = true, MinTerrainTypeChangePercent = 50 });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 50%; "
            + WaterChecksOn,
            lines);
    }

    [Fact]
    public void Config_LeavesOutTheWaterShareWhenNoWaterChangeIsCounted()
    {
        var lines = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            CountWaterHeightChanges = false,
            CountWaterAddedOrRemoved = false,
        });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 20%; "
            + "Count water height changes: off; Count water added/removed changes: off.",
            lines);
    }

    [Fact]
    public void Config_LeavesOutTheChecksThatAreSwitchedOff()
    {
        var noHeight = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes,
            CountHeightChanges = false,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 0.5f },
            ByDistance = new TerrainByDistanceSettings { Use = false },
        });
        var noType = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.VisibleObjectsAtAnyHeight,
            CountTerrainTypeChanges = false,
        });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: off; Count height changes: off; Count ground type changes: on, remove only when this share of the ground points changed type: 20%; "
            + WaterChecksOn,
            noHeight);
        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: VisibleObjectsAtAnyHeight; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; Count ground type changes: off; "
            + WaterChecksOn,
            noType);
    }

    [Fact]
    public void Config_ShowsBothTerrainMarginsWhenBothAreOn()
    {
        var lines = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 0.5f },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 64f },
        });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 20%; "
            + WaterChecksOn,
            lines);
    }

    [Fact]
    public void Config_SaysWhatIsDoneWithOtherModsObjectsTheCleanedModChangedAndThatTheirCollisionHandlingIsTheSame()
    {
        var lines = ConfigLines(_ => { });

        Assert.Equal(
            [
                "Mod to clean up: Target.esp",
                "Other mods' objects Target.esp changed: moved farther than 10 units: RestoreIfNoCollision, no resizing; replaced: not processed.",
                "  Collision handling: as for Target.esp's objects.",
            ],
            lines.Take(3));
    }

    [Fact]
    public void Config_NamesTheModeAndTheResizingOfMovedAndProcessedReplacedObjects()
    {
        var lines = ConfigLines(settings =>
        {
            settings.ChangedObjects.Moved.MovedDistanceInUnits = 25;
            settings.ChangedObjects.Moved.WhenRemoved = RestoreMode.Remove;
            settings.ChangedObjects.Replaced.Process = true;
            settings.ChangedObjects.Replaced.AttemptResizingBeforeRemoval = true;
        });

        Assert.Equal(
            "Other mods' objects Target.esp changed: moved farther than 25 units: Remove, no resizing; "
            + "replaced: RestoreIfNoCollision, resizing tried first (down to 80%, at most 360 degrees).",
            lines[1]);
    }

    [Fact]
    public void Config_SaysResizingIsTriedFirstForMovedObjectsAndLeavesOutTheReplacedModeWhileTheyAreNotProcessed()
    {
        var lines = ConfigLines(settings =>
        {
            settings.ChangedObjects.Moved.AttemptResizingBeforeRemoval = true;
            settings.ChangedObjects.Replaced.WhenRemoved = RestoreMode.Restore;
        });

        Assert.Equal(
            "Other mods' objects Target.esp changed: moved farther than 10 units: RestoreIfNoCollision, resizing tried first (down to 80%, at most 360 degrees); replaced: not processed.",
            lines[1]);
    }

    [Fact]
    public void Config_GivesTwoCollisionLinesOfOtherModsObjectsTheCleanedModChangedWhenTheirTooCloseOptionsDiffer()
    {
        var lines = ConfigLines(settings => settings.ChangedObjects.Collision.TooClose.ByDistance.DistanceInUnits = 77f);

        Assert.DoesNotContain(lines, line => line.StartsWith("  Collision handling: as for", StringComparison.Ordinal));
        Assert.StartsWith("  Collision handling: Remove objects too close to other mods' objects: ", lines[2]);
        Assert.Contains("Margin by fixed distance: on, 77 units;", lines[2]);
        Assert.StartsWith("  Collision handling: Remove objects on terrain changed by other mods: ", lines[3]);
        Assert.DoesNotContain(lines, line => line.StartsWith("  Collision handling: Shrink or turn", StringComparison.Ordinal));
    }

    [Fact]
    public void Config_NamesTheResizeLimitsOnTheLineOfEachKindThatTriesResizing()
    {
        var lines = ConfigLines(settings =>
        {
            settings.ChangedObjects.Moved.AttemptResizingBeforeRemoval = true;
            settings.ChangedObjects.Collision.ShrinkLimitPercent = 60;
        });

        Assert.Equal(
            "Other mods' objects Target.esp changed: moved farther than 10 units: RestoreIfNoCollision, resizing tried first (down to 60%, at most 360 degrees); replaced: not processed.",
            lines[1]);
        Assert.Equal("  Collision handling: as for Target.esp's objects.", lines[2]);
    }

    [Fact]
    public void Config_ComparesOnlyTheTooCloseAndTerrainOptionsWithTheStandardOnes()
    {
        var onlyStandardShrinkOff = ConfigLines(settings => settings.ShrinkOrTurn.ShrinkOrTurnObjects = false);
        var onlyLimitsChanged = ConfigLines(settings => settings.ChangedObjects.Collision.TurnLimitDegrees = 90);

        Assert.Equal("  Collision handling: as for Target.esp's objects.", onlyStandardShrinkOff[2]);
        Assert.Equal("  Collision handling: as for Target.esp's objects.", onlyLimitsChanged[2]);
        Assert.DoesNotContain("degrees", onlyLimitsChanged[1], StringComparison.Ordinal);
    }
}
