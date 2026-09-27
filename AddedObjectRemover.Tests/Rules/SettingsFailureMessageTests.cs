using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover.Tests.Rules;

public class SettingsFailureMessageTests
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Converters = { new StringEnumConverter() },
    };

    private static JsonException Deserialize(string json)
    {
        try
        {
            JsonConvert.DeserializeObject<Settings>(json, JsonSettings);
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

        var message = SettingsFailureMessage.Describe(ex);

        Assert.Contains("Also remove", message);
        Assert.Contains("FollowUpRemoval.Mode", message);
        Assert.Contains("Anchoring", message);
        Assert.Contains("Allowed values: Nothing, EverythingTouching, ObjectsSupportedByIt", message);
    }

    [Fact]
    public void WrongJsonTypeNamesTheSettingAndTheValueFound()
    {
        var ex = Deserialize("""{"WhatToCheck":{"SizeMultiplier":"abc"}}""");

        var message = SettingsFailureMessage.Describe(ex);

        Assert.Contains("Removal distance", message);
        Assert.Contains("WhatToCheck.SizeMultiplier", message);
        Assert.Contains("abc", message);
    }

    [Fact]
    public void PathIsMappedToItsDisplayNameBreadcrumb()
    {
        var setting = SettingsFailureMessage.ResolveSetting("LeftoverInvisibleObjects.CustomProtectedTypes[0]");

        Assert.NotNull(setting);
        Assert.Equal("Invisible objects left behind > Types to never remove (Custom)", setting.Value.Breadcrumb);
        Assert.Equal(typeof(InvisibleObjectKind), setting.Value.LeafType);
    }

    [Fact]
    public void AnUnrecognisedPathSegmentIsKeptAsIsWithNoAllowedValues()
    {
        var setting = SettingsFailureMessage.ResolveSetting("NoLongerAProperty");

        Assert.NotNull(setting);
        Assert.Equal("NoLongerAProperty", setting.Value.Breadcrumb);
        Assert.Null(setting.Value.LeafType);
    }
}
