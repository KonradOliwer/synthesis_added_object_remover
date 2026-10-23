using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Json;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover.Tests.Rules;

public sealed class ConfigFilesTests : IDisposable
{
    /// <summary>As Synthesis reads settings.json (Mutagen.Bethesda.Synthesis ReflectionSettingsTarget).</summary>
    private static readonly JsonSerializerSettings SynthesisJson = CreateSynthesisJson();

    private const string TownWalls = "town_walls";

    private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "aor-config-files-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataFolder)) Directory.Delete(_dataFolder, recursive: true);
    }

    private static JsonSerializerSettings CreateSynthesisJson()
    {
        var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
        settings.Converters.Add(new StringEnumConverter());
        settings.AddMutagenConverters();
        return settings;
    }

    private string GroupsFolder => Path.Combine(_dataFolder, "synthesis_added_object_remover", "groups");

    private string ConfigsFolder => Path.Combine(_dataFolder, "synthesis_added_object_remover", "configs");

    private string GroupPath(string name) => Path.Combine(GroupsFolder, name + ".json");

    private string ConfigPath(string name) => Path.Combine(ConfigsFolder, name + ".json");

    private void WriteGroup(string name, string json) => Write(GroupPath(name), json);

    private void WriteConfig(string name, string json) => Write(ConfigPath(name), json);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private ConfigsRead ReadBatch(string groupName, StandardSettings? standard = null) =>
        Program.ReadConfigs(
            new Settings { Mode = PatchMode.Batch, Batch = new BatchSettings { GroupFileName = groupName }, Standard = standard ?? new StandardSettings() },
            _dataFolder);

    private string ReadFailure(string groupName) => Assert.IsType<ConfigsRead.Failed>(ReadBatch(groupName)).Message;

    private ConfiguredSettings ReadOnlyConfig(string configJson)
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, configJson);
        return Assert.Single(Assert.IsType<ConfigsRead.Ready>(ReadBatch("group")).Configs);
    }

    private static ConfigFileNotes NotesOf(ConfiguredSettings config) => Assert.IsType<SettingsSource.ConfigFile>(config.Source).Notes;

    private const string NothingWasRun = " Nothing was run and no patch was written.";

    private const string MissingConfigAdvice = " Check the name in the group file, and that the mod with the folder is enabled in Mod Organizer 2.";

    [Fact]
    public void SpacesAroundTheTypedGroupFileNameAreIgnored()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{}");

        Assert.IsType<ConfigsRead.Ready>(ReadBatch("  group "));
    }

    [Fact]
    public void StandardModeGivesTheStandardGroupAsTheOnlyConfigAndReadsNoFile()
    {
        var standard = new StandardSettings { CleanedMod = ModKey.FromNameAndExtension("Target.esp") };

        var read = Program.ReadConfigs(new Settings { Standard = standard, Batch = new BatchSettings { GroupFileName = "ignored" } }, _dataFolder);

        var config = Assert.Single(Assert.IsType<ConfigsRead.Ready>(read).Configs);
        Assert.IsType<SettingsSource.Standard>(config.Source);
        Assert.Same(standard, config.Settings);
    }

    [Fact]
    public void AModeThatIsNotDefinedFailsTheBatch()
    {
        var read = Program.ReadConfigs(new Settings { Mode = (PatchMode)7 }, _dataFolder);

        Assert.Equal("Mode 7 is invalid; use Standard or Batch." + NothingWasRun, Assert.IsType<ConfigsRead.Failed>(read).Message);
    }

    [Fact]
    public void NoGroupFileNameFailsTheBatch()
    {
        Assert.Equal(
            "Batch mode is on, but no group file name is set (Batch > Group file name)." + NothingWasRun,
            ReadFailure("  "));
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("..\\groups\\x")]
    public void AGroupFileNameThatIsAPathFailsTheBatch(string name)
    {
        Assert.Equal(
            $"Group file name '{name}' must be a file name in {GroupsFolder}, not a path." + NothingWasRun,
            ReadFailure(name));
    }

    [Fact]
    public void AMissingGroupFileFailsNamingItsFullPath()
    {
        Assert.Equal(
            $"Group file not found: {GroupPath("nothing")} (check the name under Batch > Group file name, and that the mod with the folder is enabled in Mod Organizer 2)." + NothingWasRun,
            ReadFailure("nothing"));
    }

    [Fact]
    public void AGroupFileThatIsNotJsonFailsNamingTheFileAndTheProblem()
    {
        WriteGroup("broken", "[\"a\",\n{ not json");

        var message = ReadFailure("broken");

        Assert.StartsWith($"Group file {GroupPath("broken")} is not valid JSON: ", message);
        Assert.Contains("line 2", message);
        Assert.EndsWith(NothingWasRun, message);
    }

    [Fact]
    public void AGroupFileThatIsNotAListFailsNamingWhatWasFound()
    {
        WriteGroup("object", "{\"a\": 1}");

        Assert.Equal(
            $"Group file {GroupPath("object")} must hold a list of config names, like [\"first\", \"second\"]; found an object at line 1." + NothingWasRun,
            ReadFailure("object"));
    }

    [Fact]
    public void AGroupFileListItemThatIsNotTextFailsNamingItsLine()
    {
        WriteGroup("number", "[\n\"a\",\n5\n]");

        Assert.Equal(
            $"Group file {GroupPath("number")} must hold a list of config names, like [\"first\", \"second\"]; found a number at line 3." + NothingWasRun,
            ReadFailure("number"));
    }

    [Fact]
    public void AGroupFileThatListsNothingFails()
    {
        WriteGroup("empty", "[]");

        Assert.Equal($"Group file {GroupPath("empty")} lists no configs." + NothingWasRun, ReadFailure("empty"));
    }

    [Theory]
    [InlineData("[\"x\", \"x\"]")]
    [InlineData("[\"x\", \"X.json\"]")]
    public void AConfigListedTwiceFailsNamingTheSecondEntry(string groupJson)
    {
        WriteGroup("twice", groupJson);

        var message = ReadFailure("twice");

        Assert.StartsWith($"Group file {GroupPath("twice")} lists the config '", message);
        Assert.EndsWith("' twice." + NothingWasRun, message);
    }

    [Fact]
    public void AConfigNameThatIsAPathFailsNamingTheGroupFile()
    {
        WriteGroup("path", "[\"sub\\\\x\"]");

        Assert.Equal(
            $"Config name 'sub\\x' in group file {GroupPath("path")} must be a file name in {ConfigsFolder}, not a path." + NothingWasRun,
            ReadFailure("path"));
    }

    [Fact]
    public void AMissingConfigFileFailsNamingItAndTheGroupFile()
    {
        WriteGroup("group", "[\"gone\"]");

        Assert.Equal($"Config file not found: {ConfigPath("gone")} (listed in {GroupPath("group")})." + MissingConfigAdvice + NothingWasRun, ReadFailure("group"));
    }

    [Fact]
    public void AConfigFileThatIsNotJsonFailsNamingTheFileAndTheProblem()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"CleanedMod\": ");

        var message = ReadFailure("group");

        Assert.StartsWith($"Config file {ConfigPath(TownWalls)} is not valid JSON: ", message);
        Assert.EndsWith(NothingWasRun, message);
    }

    [Fact]
    public void AConfigFileThatIsNotAnObjectFails()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "[1]");

        Assert.Equal(
            $"Config file {ConfigPath(TownWalls)} must hold an object of settings, like {{\"CleanedMod\": \"SomeMod.esp\"}}." + NothingWasRun,
            ReadFailure("group"));
    }

    [Fact]
    public void AnEnumValueThatCannotBeReadFailsNamingTheConfigFileTheSettingTheValueAndTheAllowedValues()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"TooClose\": {\"NpcHandling\": \"Sometimes\"}}");

        var message = ReadFailure("group");

        Assert.StartsWith(
            $"Invalid setting in config file {ConfigPath(TownWalls)}: 'Remove objects too close to other mods' objects > Other mods' NPCs and creatures cause removal' ({TownWalls}.json path 'TooClose.NpcHandling').",
            message);
        Assert.Contains("Sometimes", message);
        Assert.EndsWith("Allowed values: AlwaysLikeAnyObject, OnlyWhenStuckInsideTheObject, Never." + NothingWasRun, message);
    }

    [Fact]
    public void AValueOfTheWrongJsonTypeFailsNamingTheSettingAndTheValueFound()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"TooClose\": {\"BySize\": {\"SizeMultiplier\": \"abc\"}}}");

        var message = ReadFailure("group");

        Assert.StartsWith($"Invalid setting in config file {ConfigPath(TownWalls)}: ", message);
        Assert.Contains("(town_walls.json path 'TooClose.BySize.SizeMultiplier')", message);
        Assert.Contains("abc", message);
        Assert.Contains("Expected a number.", message);
    }

    [Fact]
    public void AMisspelledPluginNameFailsNamingTheConfigFile()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"CleanedMod\": \"Foo\"}");

        var message = ReadFailure("group");

        Assert.StartsWith($"Invalid setting in config file {ConfigPath(TownWalls)}: a plugin name in ", message);
        Assert.Contains("Value found: Foo.", message);
        Assert.EndsWith("Expected a plugin file name, for example SomeMod.esp." + NothingWasRun, message);
    }

    [Fact]
    public void TheFirstProblemInGroupOrderIsTheOneReported()
    {
        WriteGroup("group", "[\"first\", \"second\", \"third\"]");
        WriteConfig("first", "{\"CleanedMod\": \"A.esp\"}");

        Assert.Equal($"Config file not found: {ConfigPath("second")} (listed in {GroupPath("group")})." + MissingConfigAdvice + NothingWasRun, ReadFailure("group"));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("group.json")]
    [InlineData("GROUP")]
    public void TheGroupFileNameWorksWithOrWithoutTheExtension(string groupName)
    {
        WriteGroup("group", "[\"first\", \"second.json\"]");
        WriteConfig("first", "{\"CleanedMod\": \"A.esp\"}");
        WriteConfig("second", "{\"CleanedMod\": \"B.esp\"}");

        var configs = Assert.IsType<ConfigsRead.Ready>(ReadBatch(groupName)).Configs;

        Assert.Equal(["A.esp", "B.esp"], configs.Select(config => config.Settings.CleanedMod));
        var second = Assert.IsType<SettingsSource.ConfigFile>(configs[1].Source);
        Assert.Equal(("second", "second.json", ConfigPath("second"), 2, 2), (second.Name, second.FileName, second.FilePath, second.Number, second.Count));
    }

    [Fact]
    public void ASettingLeftOutOfAConfigTakesItsBuiltInDefaultNotTheStandardGroupsValue()
    {
        var standard = new StandardSettings();
        standard.TooClose.BySize.SizeMultiplier = 3f;

        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"CleanedMod\": \"A.esp\"}");
        var config = Assert.Single(Assert.IsType<ConfigsRead.Ready>(ReadBatch("group", standard)).Configs);

        Assert.Equal(SettingDefaults.SizeMultiplier, config.Settings.TooClose.SizeMultiplier);
    }

    [Fact]
    public void AConfigReadsToTheSameSettingsAsSynthesisReadsTheSameText()
    {
        const string Text = """
            {
              "CleanedMod": "A.esp",
              "Trust": { "TrustedModsAndTheirMasters": ["B.esp", "C.esp"], "TrustModsLoadingBeforeCleanedMod": false },
              "TooClose": { "NpcHandling": "Never", "ByDistance": { "Use": true, "DistanceInUnits": 40 } },
              "LeftoverInvisibleObjects": { "AlwaysKeep": { "Lights": true } }
            }
            """;
        var config = ReadOnlyConfig(Text);
        var synthesis = new ReflectionSettingsTarget<StandardSettings>(Path.GetFileName(ConfigPath(TownWalls)), throwIfMissing: true) { AnchorPath = ConfigsFolder };

        Assert.Equal(JsonConvert.SerializeObject(synthesis.Value.Value, SynthesisJson), JsonConvert.SerializeObject(config.Settings, SynthesisJson));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        var config = ReadOnlyConfig("// the town\n{\n  \"CleanedMod\": \"A.esp\", /* the mod */\n}");

        Assert.Equal("A.esp", config.Settings.CleanedMod);
    }

    [Fact]
    public void UnknownKeysAtTheTopAndInsideAGroupAreNotedAndNotAFailure()
    {
        var config = ReadOnlyConfig("{\"CleanedMod\": \"A.esp\", \"Colour\": 1, \"TooClose\": {\"UseRoughBox\": true, \"BySize\": {\"Weight\": 2}}}");

        Assert.Equal(["Colour", "TooClose.UseRoughBox", "TooClose.BySize.Weight"], NotesOf(config).UnknownKeys.ToArray());
    }

    [Fact]
    public void TheRemovedLogAndReportSettingsAreNotedAsUnknownKeys()
    {
        var config = ReadOnlyConfig("{\"CleanedMod\": \"A.esp\", \"Diagnostics\": {\"DetailedLog\": true, \"WriteReportFiles\": true, \"DiagnosticsFolder\": \"x\"}}");

        Assert.Equal(["Diagnostics"], NotesOf(config).UnknownKeys.ToArray());
    }

    [Fact]
    public void ALeftOutOrNullGroupIsNotedAsItsSettingsWithTheirDefaultsAndAGroupInTheFileNotesOnlyItsMissingSettings()
    {
        var config = ReadOnlyConfig("{\"CleanedMod\": \"A.esp\", \"TerrainChange\": null, \"TooClose\": {\"BySize\": {\"SizeMultiplier\": 1.0}}}");

        var notInFile = NotesOf(config).NotInFile;

        Assert.Contains(new SettingInFile("LeftoverInvisibleObjects.RemoveLeftoverInvisibleObjects", "Invisible objects left behind > Remove invisible objects left inside other mods' objects or alone", "true"), notInFile);
        Assert.Contains(new SettingInFile("LeftoverInvisibleObjects.SurroundingObjects.SearchRadius", "Invisible objects left behind > When the cleaned mod's visible objects around it were removed > Look this far around it (game units)", "1024.0"), notInFile);
        Assert.Contains(new SettingInFile("TerrainChange.TerrainCheckedObjects", "Remove objects on terrain changed by other mods > Which of the cleaned mod's objects are checked", "\"AllObjectsIncludingInvisibleOnes\""), notInFile);
        Assert.Contains(new SettingInFile("TerrainChange.ByDistance.DistanceInUnits", "Remove objects on terrain changed by other mods > Margin by fixed distance > Margin on every side (game units)", "64.0"), notInFile);
        Assert.Contains(new SettingInFile("Trust.TrustedModsAndTheirMasters", "Which other mods can cause removals > Trusted mods, and their masters", "[]"), notInFile);
        Assert.Contains(
            new SettingInFile("TooClose.UseRoughBoxes", "Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster)", "false"),
            notInFile);
        Assert.Contains(
            new SettingInFile("TooClose.BySize.Use", "Remove objects too close to other mods' objects > Margin by object size > Use this margin", "true"),
            notInFile);
        Assert.DoesNotContain(notInFile, setting => setting.KeyPath is "TerrainChange" or "LeftoverInvisibleObjects" or "TooClose.BySize");
        Assert.DoesNotContain(notInFile, setting => setting.KeyPath is "CleanedMod" or "TooClose.BySize.SizeMultiplier");
    }

    [Fact]
    public void AnUnsetPluginIsNotedAsNullAndTheKeysFollowTheOrderOfTheSettings()
    {
        var config = ReadOnlyConfig("{}");

        var notInFile = NotesOf(config).NotInFile;

        Assert.Contains(new SettingInFile("CleanedMod", "Mod to clean up", "null"), notInFile);
        Assert.Equal(["CleanedMod", "Trust.TrustedModsAndTheirMasters", "Trust.ModsTrustedForOwnChangesOnly"], notInFile.Take(3).Select(setting => setting.KeyPath));
    }

    [Fact]
    public void KeysMatchIgnoringCaseSoTheyAreNeitherUnknownNorMissing()
    {
        var config = ReadOnlyConfig("{\"cleanedmod\": \"A.esp\", \"tooclose\": {\"useroughboxes\": true}}");

        var notes = NotesOf(config);

        Assert.Empty(notes.UnknownKeys);
        Assert.DoesNotContain(notes.NotInFile, setting => setting.KeyPath is "CleanedMod" or "TooClose" or "TooClose.UseRoughBoxes");
        Assert.Contains(new SettingInFile("CleanedMod", "Mod to clean up", "\"A.esp\""), notes.ChangedFromDefault);
        Assert.Contains(notes.ChangedFromDefault, setting => setting.KeyPath == "TooClose.UseRoughBoxes" && setting.ValueAsJson == "true");
    }

    [Fact]
    public void OnlyValuesDifferentFromTheDefaultAreNotedAsChangedAndEqualNumbersAreTheSame()
    {
        var config = ReadOnlyConfig(
            """
            {
              "CleanedMod": "A.esp",
              "TooClose": {
                "NpcHandling": "Never",
                "UseRoughBoxes": false,
                "BySize": { "SizeMultiplier": 1.0 },
                "ByDistance": { "DistanceInUnits": 50.0 }
              },
              "Trust": { "TrustedModsAndTheirMasters": ["B.esp"], "TrustModsLoadingBeforeCleanedMod": true },
              "TerrainChange": { "MinTerrainTypeChangePercent": 20 },
              "LeftoverInvisibleObjects": { "RemoveLeftoverInvisibleObjects": false }
            }
            """);

        var changed = NotesOf(config).ChangedFromDefault;

        Assert.Equal(
            ["CleanedMod", "Trust.TrustedModsAndTheirMasters", "TooClose.BySize.SizeMultiplier", "TooClose.NpcHandling", "LeftoverInvisibleObjects.RemoveLeftoverInvisibleObjects"],
            changed.Select(setting => setting.KeyPath));
        Assert.Equal(["\"A.esp\"", "[\"B.esp\"]", "1.0", "\"Never\"", "false"], changed.Select(setting => setting.ValueAsJson));
        Assert.Contains(
            new SettingInFile("TooClose.BySize.SizeMultiplier", "Remove objects too close to other mods' objects > Margin by object size > Margin on every side (× object size)", "1.0"),
            changed);
    }

    [Fact]
    public void TheWaterSettingsOfTheTerrainGroupAreReadByTheirSettingsKeys()
    {
        var config = ReadOnlyConfig(
            """
            {
              "CleanedMod": "A.esp",
              "TerrainChange": { "CountWaterHeightChanges": false, "WaterHeightChangeTolerance": 40, "CountWaterAddedOrRemoved": false, "MinWaterCoverChangePercent": 60 }
            }
            """);

        var terrain = config.Settings.TerrainChange;

        Assert.Equal((false, 40f, false, 60), (terrain.CountWaterHeightChanges, terrain.WaterHeightChangeTolerance, terrain.CountWaterAddedOrRemoved, terrain.MinWaterCoverChangePercent));
        Assert.Empty(NotesOf(config).UnknownKeys);
    }

    [Fact]
    public void ANullListIsTheDefaultOfAListSetting()
    {
        var config = ReadOnlyConfig("{\"CleanedMod\": \"A.esp\", \"Trust\": {\"TrustedModsAndTheirMasters\": null}}");

        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "Trust.TrustedModsAndTheirMasters");
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(".json")]
    [InlineData("..json")]
    [InlineData("town ")]
    [InlineData("town.")]
    [InlineData("town .json")]
    public void AConfigNameThatWindowsWouldMisplaceFailsNamingTheGroupFile(string name)
    {
        WriteGroup("odd", $"[\"{name}\"]");

        Assert.Equal(
            $"Config name '{name}' in group file {GroupPath("odd")} must be a file name in {ConfigsFolder}, not a path." + NothingWasRun,
            ReadFailure("odd"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".json")]
    [InlineData("town.")]
    public void AGroupNameThatWindowsWouldMisplaceFailsAsNotAFileName(string name)
    {
        Assert.Equal($"Group file name '{name}' must be a file name in {GroupsFolder}, not a path." + NothingWasRun, ReadFailure(name));
    }

    [Fact]
    public void ALockedConfigFileFailsNamingTheFile()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{}");
        using var lockedFile = new FileStream(ConfigPath(TownWalls), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var message = ReadFailure("group");

        Assert.StartsWith($"Config file {ConfigPath(TownWalls)} could not be read: ", message);
        Assert.EndsWith(NothingWasRun, message);
    }

    [Fact]
    public void ALockedGroupFileFailsNamingTheFile()
    {
        WriteGroup("group", "[\"a\"]");
        using var lockedFile = new FileStream(GroupPath("group"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var message = ReadFailure("group");

        Assert.StartsWith($"Group file {GroupPath("group")} could not be read: ", message);
        Assert.EndsWith(NothingWasRun, message);
    }

    /// <summary>The Synthesis settings screen saves a plugin picker left unset as "".</summary>
    private const string UnsetPlugins = "\"CleanedMod\": \"\", \"Trust\": { \"TrustedModsAndTheirMasters\": [\"\"] }";

    private T ReadLikeSynthesis<T>(string fileName, string json) where T : class, new()
    {
        Write(Path.Combine(_dataFolder, fileName), json);
        return new ReflectionSettingsTarget<T>(fileName, throwIfMissing: true) { AnchorPath = _dataFolder }.Value.Value;
    }

    private static BatchRequest BuildAll(ConfigsRead read)
    {
        var target = new PluginName("A.esp");
        var patch = new PluginName("Patch.esp");
        var facts = TestLoadOrderPlugins.Of(new KnownPluginNames([target, patch]), patch, [new PluginListing(target, true, []), new PluginListing(patch, true, [])]);
        return SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), facts, detailedLogAndReports: false, onlyWarnWhenRequiringModLoadsAfterPatch: false, "/out", 1);
    }

    [Fact]
    public void AnUnsetPluginPickerInSettingsJsonReadsAsNotChosen()
    {
        var settings = ReadLikeSynthesis<Settings>(Program.SettingsFileName, $"{{ \"Mode\": \"Standard\", \"Standard\": {{ {UnsetPlugins} }} }}");

        Assert.True(settings.Standard.CleanedMod.IsNull);
        Assert.True(Assert.Single(settings.Standard.Trust.TrustedModsAndTheirMasters).IsNull);
        Assert.Null(((ISettingsValues)settings.Standard).CleanedMod);
        Assert.Empty(((ISettingsValues)settings.Standard).Trust.TrustedModsAndTheirMasters);
    }

    [Fact]
    public void StandardModeWithAnUnsetPluginPickerInSettingsJsonStopsAsNoTargetSet()
    {
        var settings = ReadLikeSynthesis<Settings>(Program.SettingsFileName, $"{{ \"Mode\": \"Standard\", \"Standard\": {{ {UnsetPlugins} }} }}");

        var batch = Assert.IsType<BatchRequest.Ready>(BuildAll(Program.ReadConfigs(settings, _dataFolder)));

        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), Assert.IsType<RunRequest.Stop>(Assert.Single(batch.Requests)).Reason);
    }

    [Fact]
    public void BatchModeIgnoresAnUnsetPluginPickerInTheStandardGroupOfSettingsJson()
    {
        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"CleanedMod\": \"A.esp\"}");
        var settings = ReadLikeSynthesis<Settings>(
            Program.SettingsFileName, $"{{ \"Mode\": \"Batch\", \"Batch\": {{ \"GroupFileName\": \"group\" }}, \"Standard\": {{ {UnsetPlugins} }} }}");

        var read = Program.ReadConfigs(settings, _dataFolder);

        Assert.Equal("A.esp", Assert.Single(Assert.IsType<ConfigsRead.Ready>(read).Configs).Settings.CleanedMod);
        Assert.IsType<RunRequest.Ready>(Assert.Single(Assert.IsType<BatchRequest.Ready>(BuildAll(read)).Requests));
    }

    [Fact]
    public void AConfigFileWithAnEmptyPluginNameReadsLikeSynthesisAndFailsTheBatchAsNoTargetSet()
    {
        var config = ReadOnlyConfig($"{{ {UnsetPlugins} }}");
        var synthesis = new ReflectionSettingsTarget<StandardSettings>(Path.GetFileName(ConfigPath(TownWalls)), throwIfMissing: true) { AnchorPath = ConfigsFolder };

        Assert.Equal(JsonConvert.SerializeObject(synthesis.Value.Value, SynthesisJson), JsonConvert.SerializeObject(config.Settings, SynthesisJson));
        Assert.Empty(config.Settings.Trust.TrustedModsAndTheirMasters);
        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "CleanedMod");
        Assert.Equal(
            $"Config file {ConfigPath(TownWalls)} sets no mod to clean up (CleanedMod). Nothing was run and no patch was written.",
            Assert.IsType<BatchRequest.Failed>(BuildAll(new ConfigsRead.Ready([config]))).Message);
    }

    [Fact]
    public void AnEmptyListIsTheDefaultOfAListSetting()
    {
        var config = ReadOnlyConfig("{\"CleanedMod\": \"A.esp\", \"Trust\": {\"TrustedModsAndTheirMasters\": []}}");

        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "Trust.TrustedModsAndTheirMasters");
    }
}
