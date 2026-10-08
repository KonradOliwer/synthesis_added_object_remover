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
    public void DefaultsAreWithinTheirValidRanges()
    {
        var leftovers = new LeftoverInvisibleObjectSettings();
        Assert.Equal(leftovers.DirectionThresholdPercent, SettingCorrections.WholeTens(leftovers.DirectionThresholdPercent, "direction", []));
        Assert.Equal(leftovers.RemovedDirectionsPercent, SettingCorrections.WholeTens(leftovers.RemovedDirectionsPercent, "removed", []));
        Assert.Equal(leftovers.OccupiedDirectionsPercent, SettingCorrections.WholeTens(leftovers.OccupiedDirectionsPercent, "occupied", []));
        Assert.Equal(ZoneShape.ObjectShape, new CheckSettings().ZoneShape);
        Assert.Equal(NpcHandling.OnlyWhenStuckInObject, new IgnoreSettings().NpcHandling);
        Assert.Equal(FollowUpRemovalMode.EverythingTouching, new FollowUpRemovalSettings().Mode);
    }
}
