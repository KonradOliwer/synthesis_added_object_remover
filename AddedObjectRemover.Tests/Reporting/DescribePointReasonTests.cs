using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Tests.Reporting;

public class DescribePointReasonTests
{
    [Fact]
    public void AnEmptyRaceLinkSaysTheNpcHasNoRaceSet()
    {
        var reason = new PointReason(PointReasonKind.RaceNotFound, RecordKey.Null, RaceEditorId: null);

        Assert.Equal("has no race set", Describe.PointReason(reason));
    }

    [Fact]
    public void ARaceLinkToAMissingRecordNamesIt()
    {
        var reason = new PointReason(PointReasonKind.RaceNotFound, new RecordKey(new PluginName("Race.esp"), 0x12), RaceEditorId: null);

        Assert.Equal("race 000012:Race.esp not found", Describe.PointReason(reason));
    }

    [Fact]
    public void ATimedSuffixPrefixesTheDurationWithInWhenDetailed() =>
        Assert.Equal(" in 2.0s", Describe.TimedSuffix(TimeSpan.FromSeconds(2), new ReportContext(null!, null!, Detailed: true)));
}
