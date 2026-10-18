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
        var standard = new StandardSettings { TargetPlugin = ModKey.FromNameAndExtension("Target.esp") };

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
        WriteConfig(TownWalls, "{\"TargetPlugin\": ");

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
            $"Config file {ConfigPath(TownWalls)} must hold an object of settings, like {{\"TargetPlugin\": \"SomeMod.esp\"}}." + NothingWasRun,
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
        WriteConfig(TownWalls, "{\"TargetPlugin\": \"Foo\"}");

        var message = ReadFailure("group");

        Assert.StartsWith($"Invalid setting in config file {ConfigPath(TownWalls)}: a plugin name in ", message);
        Assert.Contains("Value found: Foo.", message);
        Assert.EndsWith("Expected a plugin file name, for example SomeMod.esp." + NothingWasRun, message);
    }

    [Fact]
    public void TheFirstProblemInGroupOrderIsTheOneReported()
    {
        WriteGroup("group", "[\"first\", \"second\", \"third\"]");
        WriteConfig("first", "{\"TargetPlugin\": \"A.esp\"}");

        Assert.Equal($"Config file not found: {ConfigPath("second")} (listed in {GroupPath("group")})." + MissingConfigAdvice + NothingWasRun, ReadFailure("group"));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("group.json")]
    [InlineData("GROUP")]
    public void TheGroupFileNameWorksWithOrWithoutTheExtension(string groupName)
    {
        WriteGroup("group", "[\"first\", \"second.json\"]");
        WriteConfig("first", "{\"TargetPlugin\": \"A.esp\"}");
        WriteConfig("second", "{\"TargetPlugin\": \"B.esp\"}");

        var configs = Assert.IsType<ConfigsRead.Ready>(ReadBatch(groupName)).Configs;

        Assert.Equal(["A.esp", "B.esp"], configs.Select(config => config.Settings.TargetPlugin));
        var second = Assert.IsType<SettingsSource.ConfigFile>(configs[1].Source);
        Assert.Equal(("second", "second.json", ConfigPath("second"), 2, 2), (second.Name, second.FileName, second.FilePath, second.Number, second.Count));
    }

    [Fact]
    public void ASettingLeftOutOfAConfigTakesItsBuiltInDefaultNotTheStandardGroupsValue()
    {
        var standard = new StandardSettings();
        standard.TooClose.BySize.SizeMultiplier = 3f;

        WriteGroup("group", $"[\"{TownWalls}\"]");
        WriteConfig(TownWalls, "{\"TargetPlugin\": \"A.esp\"}");
        var config = Assert.Single(Assert.IsType<ConfigsRead.Ready>(ReadBatch("group", standard)).Configs);

        Assert.Equal(SettingDefaults.SizeMultiplier, config.Settings.TooClose.SizeMultiplier);
    }

    [Fact]
    public void AConfigReadsToTheSameSettingsAsSynthesisReadsTheSameText()
    {
        const string Text = """
            {
              "TargetPlugin": "A.esp",
              "WhatToIgnore": { "ExcludedPlugins": ["B.esp", "C.esp"], "CompatibilityPatches": null },
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
        var config = ReadOnlyConfig("// the town\n{\n  \"TargetPlugin\": \"A.esp\", /* the mod */\n}");

        Assert.Equal("A.esp", config.Settings.TargetPlugin);
    }

    [Fact]
    public void UnknownKeysAtTheTopAndInsideAGroupAreNotedAndNotAFailure()
    {
        var config = ReadOnlyConfig("{\"TargetPlugin\": \"A.esp\", \"Colour\": 1, \"TooClose\": {\"UseRoughBox\": true, \"BySize\": {\"Weight\": 2}}}");

        Assert.Equal(["Colour", "TooClose.UseRoughBox", "TooClose.BySize.Weight"], NotesOf(config).UnknownKeys.ToArray());
    }

    [Fact]
    public void TheRemovedLogAndReportSettingsAreNotedAsUnknownKeys()
    {
        var config = ReadOnlyConfig("{\"TargetPlugin\": \"A.esp\", \"Diagnostics\": {\"DetailedLog\": true, \"WriteReportFiles\": true, \"DiagnosticsFolder\": \"x\"}}");

        Assert.Equal(["Diagnostics"], NotesOf(config).UnknownKeys.ToArray());
    }

    [Fact]
    public void AGroupLeftOutOrNullIsNotedOnceAndAGroupInTheFileNotesOnlyItsMissingSettings()
    {
        var config = ReadOnlyConfig("{\"TargetPlugin\": \"A.esp\", \"TerrainChange\": null, \"TooClose\": {\"BySize\": {\"SizeMultiplier\": 1.0}}}");

        var notInFile = NotesOf(config).NotInFile;

        Assert.Contains(new SettingInFile("LeftoverInvisibleObjects", "Invisible objects left behind", true, null), notInFile);
        Assert.Contains(new SettingInFile("TerrainChange", "Remove objects on terrain changed by other mods", true, null), notInFile);
        Assert.Contains(
            new SettingInFile("TooClose.UseRoughBoxes", "Remove objects too close to other mods' objects > Use rough boxes instead of exact shapes (faster)", false, null),
            notInFile);
        Assert.Contains(
            new SettingInFile("TooClose.BySize.Use", "Remove objects too close to other mods' objects > Margin by object size > Use this margin", false, null),
            notInFile);
        Assert.DoesNotContain(notInFile, setting => setting.KeyPath.StartsWith("LeftoverInvisibleObjects.", StringComparison.Ordinal));
        Assert.DoesNotContain(notInFile, setting => setting.KeyPath is "TargetPlugin" or "TooClose.BySize.SizeMultiplier" or "TooClose.BySize");
    }

    [Fact]
    public void KeysMatchIgnoringCaseSoTheyAreNeitherUnknownNorMissing()
    {
        var config = ReadOnlyConfig("{\"targetplugin\": \"A.esp\", \"tooclose\": {\"useroughboxes\": true}}");

        var notes = NotesOf(config);

        Assert.Empty(notes.UnknownKeys);
        Assert.DoesNotContain(notes.NotInFile, setting => setting.KeyPath is "TargetPlugin" or "TooClose" or "TooClose.UseRoughBoxes");
        Assert.Contains(new SettingInFile("TargetPlugin", "Mod to clean up", false, "\"A.esp\""), notes.ChangedFromDefault);
        Assert.Contains(notes.ChangedFromDefault, setting => setting.KeyPath == "TooClose.UseRoughBoxes" && setting.ValueAsWritten == "true");
    }

    [Fact]
    public void OnlyValuesDifferentFromTheDefaultAreNotedAsChangedAndEqualNumbersAreTheSame()
    {
        var config = ReadOnlyConfig(
            """
            {
              "TargetPlugin": "A.esp",
              "TooClose": {
                "NpcHandling": "Never",
                "UseRoughBoxes": false,
                "BySize": { "SizeMultiplier": 1.0 },
                "ByDistance": { "DistanceInUnits": 32.0 }
              },
              "WhatToIgnore": { "ExcludedPlugins": ["B.esp"], "IgnoreTargetMasters": true },
              "TerrainChange": { "MinTerrainTypeChangePercent": 20 },
              "LeftoverInvisibleObjects": { "RemoveLeftoverInvisibleObjects": false }
            }
            """);

        var changed = NotesOf(config).ChangedFromDefault;

        Assert.Equal(
            ["TargetPlugin", "WhatToIgnore.ExcludedPlugins", "TooClose.BySize.SizeMultiplier", "TooClose.NpcHandling", "LeftoverInvisibleObjects.RemoveLeftoverInvisibleObjects"],
            changed.Select(setting => setting.KeyPath));
        Assert.Equal(["\"A.esp\"", "[\"B.esp\"]", "1.0", "\"Never\"", "false"], changed.Select(setting => setting.ValueAsWritten));
        Assert.Contains(
            new SettingInFile("TooClose.BySize.SizeMultiplier", "Remove objects too close to other mods' objects > Margin by object size > Margin on every side (× object size)", false, "1.0"),
            changed);
    }

    [Fact]
    public void ANullListIsTheDefaultOfAListSetting()
    {
        var config = ReadOnlyConfig("{\"TargetPlugin\": \"A.esp\", \"WhatToIgnore\": {\"ExcludedPlugins\": null}}");

        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "WhatToIgnore.ExcludedPlugins");
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
    private const string UnsetPlugins = "\"TargetPlugin\": \"\", \"WhatToIgnore\": { \"ExcludedPlugins\": [\"\"] }";

    private T ReadLikeSynthesis<T>(string fileName, string json) where T : class, new()
    {
        Write(Path.Combine(_dataFolder, fileName), json);
        return new ReflectionSettingsTarget<T>(fileName, throwIfMissing: true) { AnchorPath = _dataFolder }.Value.Value;
    }

    private static BatchRequest BuildAll(ConfigsRead read)
    {
        var target = new PluginName("A.esp");
        var patch = new PluginName("Patch.esp");
        var facts = new LoadOrderPlugins(new KnownPluginNames([target, patch]), patch, [new PluginListing(target, true, []), new PluginListing(patch, true, [])]);
        return SettingsValidator.BuildAll(read, new PluginNameParsingPlugin(), facts, detailedLogAndReports: false, "/out", 1);
    }

    [Fact]
    public void AnUnsetPluginPickerInSettingsJsonReadsAsNotChosen()
    {
        var settings = ReadLikeSynthesis<Settings>(Program.SettingsFileName, $"{{ \"Mode\": \"Standard\", \"Standard\": {{ {UnsetPlugins} }} }}");

        Assert.True(settings.Standard.TargetPlugin.IsNull);
        Assert.True(Assert.Single(settings.Standard.WhatToIgnore.ExcludedPlugins).IsNull);
        Assert.Null(((ISettingsValues)settings.Standard).TargetPlugin);
        Assert.Empty(((ISettingsValues)settings.Standard).WhatToIgnore.ExcludedPlugins);
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
        WriteConfig(TownWalls, "{\"TargetPlugin\": \"A.esp\"}");
        var settings = ReadLikeSynthesis<Settings>(
            Program.SettingsFileName, $"{{ \"Mode\": \"Batch\", \"Batch\": {{ \"GroupFileName\": \"group\" }}, \"Standard\": {{ {UnsetPlugins} }} }}");

        var read = Program.ReadConfigs(settings, _dataFolder);

        Assert.Equal("A.esp", Assert.Single(Assert.IsType<ConfigsRead.Ready>(read).Configs).Settings.TargetPlugin);
        Assert.IsType<RunRequest.Ready>(Assert.Single(Assert.IsType<BatchRequest.Ready>(BuildAll(read)).Requests));
    }

    [Fact]
    public void AConfigFileWithAnEmptyPluginNameReadsLikeSynthesisAndFailsTheBatchAsNoTargetSet()
    {
        var config = ReadOnlyConfig($"{{ {UnsetPlugins} }}");
        var synthesis = new ReflectionSettingsTarget<StandardSettings>(Path.GetFileName(ConfigPath(TownWalls)), throwIfMissing: true) { AnchorPath = ConfigsFolder };

        Assert.Equal(JsonConvert.SerializeObject(synthesis.Value.Value, SynthesisJson), JsonConvert.SerializeObject(config.Settings, SynthesisJson));
        Assert.Empty(config.Settings.WhatToIgnore.ExcludedPlugins);
        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "TargetPlugin");
        Assert.Equal(
            $"Config file {ConfigPath(TownWalls)} sets no mod to clean up (TargetPlugin). Nothing was run and no patch was written.",
            Assert.IsType<BatchRequest.Failed>(BuildAll(new ConfigsRead.Ready([config]))).Message);
    }

    [Fact]
    public void AnEmptyListIsTheDefaultOfAListSetting()
    {
        var config = ReadOnlyConfig("{\"TargetPlugin\": \"A.esp\", \"WhatToIgnore\": {\"ExcludedPlugins\": []}}");

        Assert.DoesNotContain(NotesOf(config).ChangedFromDefault, setting => setting.KeyPath == "WhatToIgnore.ExcludedPlugins");
    }
}
