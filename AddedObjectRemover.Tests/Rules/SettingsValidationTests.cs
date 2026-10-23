namespace AddedObjectRemover.Tests.Rules;

public class SettingsValidationTests
{
    [Theory]
    [InlineData(50, 50)]
    [InlineData(44, 40)]
    [InlineData(45, 50)]
    [InlineData(15, 20)]
    [InlineData(5, 10)]
    [InlineData(-30, 10)]
    [InlineData(100, 100)]
    [InlineData(105, 100)]
    [InlineData(int.MaxValue, 100)]
    public void PercentIsClampedAndRoundedToWholeTens(int percent, int expected) =>
        Assert.Equal(expected, SettingCorrections.WholeTens(percent, "test percent", []));

    [Theory]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0f, 0f)]
    [InlineData(5f, 5f)]
    [InlineData(-1f, 0f)]
    [InlineData(7f, 5f)]
    [InlineData(float.NaN, 0.5f)]
    [InlineData(float.PositiveInfinity, 5f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public void NumberIsClampedAndNaNBecomesTheDefault(float value, float expected) =>
        Assert.Equal(expected, SettingCorrections.Clamp(value, 0f, 5f, SettingDefaults.SizeMultiplier, "test number", []));

    [Fact]
    public void AnUnknownPluginListPlaceStopsTheBatch() =>
        Assert.Equal(
            "Could not find the game's plugin list file (Plugins.txt in the game's folder under your local application data). Nothing was run and no patch was written.",
            SettingsValidator.StopOnUnusablePluginList(new PluginListProblem.PathUnknown()).Message);

    [Fact]
    public void AMissingPluginListFileStopsTheBatchNamingTheFile() =>
        Assert.Equal(
            "The plugin list file C:/game/Plugins.txt does not exist. Nothing was run and no patch was written.",
            SettingsValidator.StopOnUnusablePluginList(new PluginListProblem.FileMissing("C:/game/Plugins.txt")).Message);

    [Fact]
    public void AMalformedPluginListLineStopsTheBatchNamingTheFileAndTheLine() =>
        Assert.Equal(
            "The plugin list file C:/game/Plugins.txt has a malformed entry at line 4. Correct or remove that line. Nothing was run and no patch was written.",
            SettingsValidator.StopOnUnusablePluginList(new PluginListProblem.MalformedLine("C:/game/Plugins.txt", 4)).Message);

    [Fact]
    public void ALoadedPluginMissingFromThePluginListStopsTheBatchNamingThePluginAndTheFile() =>
        Assert.Equal(
            "Second.esp is loaded by Synthesis but is not in the plugin list file C:/game/Plugins.txt. Nothing was run and no patch was written.",
            SettingsValidator.StopOnUnusablePluginList(new PluginListProblem.PluginNotListed("C:/game/Plugins.txt", new PluginName("Second.esp"))).Message);

    [Fact]
    public void ALoadedPluginListedAfterThePatchStopsTheBatchNamingThePluginThePatchAndTheFile() =>
        Assert.Equal(
            "Second.esp is loaded by Synthesis above the patch Patch.esp but is listed after it in the plugin list file C:/game/Plugins.txt. Nothing was run and no patch was written.",
            SettingsValidator.StopOnUnusablePluginList(
                new PluginListProblem.PluginListedAfterPatch("C:/game/Plugins.txt", new PluginName("Second.esp"), new PluginName("Patch.esp"))).Message);

    [Fact]
    public void DefaultsAreWithinTheirValidRanges()
    {
        var surroundings = new LeftoverInvisibleObjectSettings().SurroundingObjects;
        Assert.Equal(surroundings.DirectionThresholdPercent, SettingCorrections.WholeTens(surroundings.DirectionThresholdPercent, "direction", []));
        Assert.Equal(surroundings.RemovedDirectionsPercent, SettingCorrections.WholeTens(surroundings.RemovedDirectionsPercent, "removed", []));
        Assert.Equal(surroundings.OccupiedDirectionsPercent, SettingCorrections.WholeTens(surroundings.OccupiedDirectionsPercent, "occupied", []));
        Assert.False(new TooCloseRemovalSettings().UseRoughBoxes);
        Assert.Equal(NpcHandling.OnlyWhenStuckInsideTheObject, new TooCloseRemovalSettings().NpcHandling);
        Assert.Equal(FollowUpRemovalMode.OnlyWhatLosesItsSupport, new FollowUpRemovalSettings().Mode);
    }
}
