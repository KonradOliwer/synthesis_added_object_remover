using Mutagen.Bethesda.Json;
using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover.Tests.Rules;

public class SettingsFailureMessageTests
{
    /// <summary>As Synthesis reads settings.json (Mutagen.Bethesda.Synthesis ReflectionSettingsTarget).</summary>
    private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();

    private static JsonSerializerSettings CreateJsonSettings()
    {
        var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
        settings.Converters.Add(new StringEnumConverter());
        settings.AddMutagenConverters();
        return settings;
    }

    [Fact]
    public void ATargetPluginOfTheWrongJsonTypeNamesTheSettingAndAsksForAPluginFileName()
    {
        var message = SettingsFailureMessage.Describe(Deserialize("""{"TargetPlugin":[]}"""), Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains("'Standard > Mod to clean up' (settings.json path 'Standard.TargetPlugin')", message);
        Assert.EndsWith("Expected a plugin file name, for example SomeMod.esp.", message);
    }

    /// <param name="standardJson">The content of the Standard group; the settings file holds it under "Standard".</param>
    private static string InStandardGroup(string standardJson) => "{\"Standard\":" + standardJson + "}";

    private static JsonException Deserialize(string standardJson)
    {
        try
        {
            JsonConvert.DeserializeObject<Settings>(InStandardGroup(standardJson), JsonSettings);
        }
        catch (JsonException ex)
        {
            return ex;
        }
        throw new InvalidOperationException("Expected settings deserialization to fail.");
    }

    [Fact]
    public void UnknownEnumValueNamesTheSettingTheValueAndTheAllowedOptions()
    {
        var ex = Deserialize("""{"FollowUpRemoval":{"Mode":"Anchoring"}}""");

        var message = SettingsFailureMessage.Describe(ex, Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains("When an object is removed, also remove", message);
        Assert.Contains("Standard.FollowUpRemoval.Mode", message);
        Assert.Contains("Anchoring", message);
        Assert.Contains("Allowed values: Nothing, EverythingTouchingIt, OnlyWhatLosesItsSupport", message);
    }

    [Theory]
    [InlineData("""{"TooClose":{"NpcHandling":"Ignore"}}""", "Standard.TooClose.NpcHandling", "Allowed values: AlwaysLikeAnyObject, OnlyWhenStuckInsideTheObject, Never")]
    [InlineData(
        """{"TerrainChange":{"TerrainCheckedObjects":"AllObjects"}}""",
        "Standard.TerrainChange.TerrainCheckedObjects",
        "Allowed values: VisibleObjectsOnTheGround, VisibleObjectsAtAnyHeight, AllObjectsIncludingInvisibleOnes")]
    public void UnknownEnumValueInAnotherGroupNamesItsPathAndAllowedOptions(string json, string path, string allowed)
    {
        var message = SettingsFailureMessage.Describe(Deserialize(json), Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains(path, message);
        Assert.Contains(allowed, message);
    }

    [Fact]
    public void WrongJsonTypeNamesTheSettingAndTheValueFound()
    {
        var ex = Deserialize("""{"TooClose":{"BySize":{"SizeMultiplier":"abc"}}}""");

        var message = SettingsFailureMessage.Describe(ex, Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains("Margin by object size > Margin on every side", message);
        Assert.Contains("Standard.TooClose.BySize.SizeMultiplier", message);
        Assert.Contains("abc", message);
    }

    [Fact]
    public void PluginListGivenASingleTextNamesTheSettingAndTheValueFound()
    {
        var ex = Deserialize("""{"WhatToIgnore":{"ExcludedPlugins":"SomeMod.esp"}}""");

        var message = SettingsFailureMessage.Describe(ex, Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains("Standard.WhatToIgnore.ExcludedPlugins", message);
        Assert.Contains("Never count these mods", message);
        Assert.Contains("SomeMod.esp", message);
        Assert.EndsWith("Expected a plugin file name, for example SomeMod.esp.", message);
    }

    [Theory]
    [InlineData("""{"TargetPlugin":"Foo"}""")]
    [InlineData("""{"WhatToIgnore":{"ExcludedPlugins":["Bar.esp","Foo"]}}""")]
    public void AMisspelledPluginNameStopsTheLoadNamingTheValueAndAskingForAPluginFileName(string json)
    {
        var ex = Assert.Throws<SettingsUnreadableException>(
            () => SettingsLoading.Load(() => JsonConvert.DeserializeObject<Settings>(InStandardGroup(json), JsonSettings), Program.SettingsSchema, Program.SettingsFileName));

        Assert.Contains("'Standard > Mod to clean up'", ex.Message);
        Assert.Contains("'Standard > Other mods that never cause removals > Never count these mods'", ex.Message);
        Assert.Contains("Value found: Foo.", ex.Message);
        Assert.EndsWith("Expected a plugin file name, for example SomeMod.esp.", ex.Message);
    }

    [Fact]
    public void AnArgumentFailureThatIsNotAPluginNameIsNotHidden()
    {
        Assert.Throws<ArgumentException>(
            () => SettingsLoading.Load<int>(() => throw new ArgumentException("a bug of ours"), Program.SettingsSchema, Program.SettingsFileName));
    }

    [Fact]
    public void APartlyWrittenTooCloseGroupKeepsTheTooCloseDefaults()
    {
        var settings = Read("""{"TooClose":{"ByDistance":{"Use":true},"BySize":{"SizeMultiplier":1}}}""");

        Assert.True(settings.TooClose.ByDistance.Use);
        Assert.Equal(32f, settings.TooClose.ByDistance.DistanceInUnits);
        Assert.True(settings.TooClose.BySize.Use);
        Assert.Equal(1f, settings.TooClose.BySize.SizeMultiplier);
    }

    [Fact]
    public void APartlyWrittenTerrainGroupKeepsTheTerrainDefaults()
    {
        var settings = Read("""{"TerrainChange":{"ByDistance":{"Use":true},"BySize":{"SizeMultiplier":1}}}""");

        Assert.True(settings.TerrainChange.ByDistance.Use);
        Assert.Equal(64f, settings.TerrainChange.ByDistance.DistanceInUnits);
        Assert.False(settings.TerrainChange.BySize.Use);
    }

    [Fact]
    public void ANullMarginGroupMeansItsGroupsDefaults()
    {
        var settings = Read("""{"TooClose":{"ByDistance":null},"TerrainChange":{"ByDistance":null}}""");

        Assert.Equal(32f, ((ITooCloseSettingsValues)settings.TooClose).DistanceInUnits);
        Assert.Equal(64f, ((ITerrainChangeSettingsValues)settings.TerrainChange).AreaDistanceInUnits);
    }

    private static StandardSettings Read(string json) => JsonConvert.DeserializeObject<StandardSettings>(json, JsonSettings)!;

    [Fact]
    public void SyntaxErrorOutsideAnySettingNamesItsLineAndPosition()
    {
        var ex = Assert.Throws<JsonReaderException>(() => JsonConvert.DeserializeObject<Settings>("{\n,}", JsonSettings));

        var message = SettingsFailureMessage.Describe(ex, Program.SettingsSchema, Program.SettingsFileName);

        Assert.Contains("the settings file at line 2, position", message);
    }

    [Fact]
    public void PathIsMappedToItsDisplayNameBreadcrumb()
    {
        var setting = SettingsFailureMessage.ResolveSetting("Standard.WhatToIgnore.ExcludedPlugins[0]", Program.SettingsSchema);

        Assert.NotNull(setting);
        Assert.Equal("Standard > Other mods that never cause removals > Never count these mods", setting.Value.Breadcrumb);
        Assert.Equal(typeof(ModKey), setting.Value.LeafType);
    }

    [Fact]
    public void AnUnrecognisedPathSegmentIsKeptAsIsWithNoAllowedValues()
    {
        var setting = SettingsFailureMessage.ResolveSetting("NoLongerAProperty", Program.SettingsSchema);

        Assert.NotNull(setting);
        Assert.Equal("NoLongerAProperty", setting.Value.Breadcrumb);
        Assert.Null(setting.Value.LeafType);
    }
}
