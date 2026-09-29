namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The follow-up result's removed and held counts are the numbers the golden logs show in
/// "Touching objects: N removed … K kept as referenced." and "Anchoring: N removed … K kept as referenced.".
/// </summary>
public class FollowUpCountsTests
{
    private const int Workers = 8;

    [Theory]
    [InlineData(SettingsVariants.TouchWithLeftoversAndRelocation, 4, 1)]
    [InlineData(SettingsVariants.Support, 1, 0)]
    public void CountsMatchTheGoldenLog(string variant, int removed, int held)
    {
        var followUp = RunFollowUp(SettingsVariants.Of(variant));

        Assert.Equal(removed, followUp.CountRemovedByRule());
        Assert.Equal(held, followUp.CountHeld());
    }

    private static FollowUpResult RunFollowUp(Settings settings) => FixtureRun.Execute(settings, Workers).FollowUp;
}
