using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The also-remove result holds the expected removed and held counts for the end-to-end scenes. (The log text
/// that prints them is checked against the saved expected-output files by the end-to-end tests.)
/// </summary>
public class RestingObjectsCountsTests
{
    private const int Workers = 8;

    [Theory]
    [InlineData(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, 4, 1)]
    [InlineData(SettingsVariants.Support, 1, 0)]
    public void TheRestingObjectsResultHoldsTheExpectedCounts(string variant, int removed, int held)
    {
        var alsoRemove = RunRestingObjects(SettingsVariants.Of(variant));

        Assert.Equal(removed, RestingObjectsRemovals.CountByRule(alsoRemove));
        Assert.Equal(held, alsoRemove.CountKept());
    }

    private static RestingObjectsResult RunRestingObjects(Settings settings) => FixtureRun.Execute(settings, Workers).RestingObjects;
}
