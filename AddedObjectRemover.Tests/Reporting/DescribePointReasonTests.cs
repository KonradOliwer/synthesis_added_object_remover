using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Tests.Reporting;

public class DescribePointReasonTests
{
    [Fact]
    public void NullRaceLinkPrintsAsNull()
    {
        var reason = new PointReason(PointReasonKind.RaceNotFound, RecordKey.Null, RaceEditorId: null);

        Assert.Equal("race Null not found", Describe.PointReason(reason));
    }

    [Fact]
    public void ATimedSuffixPrefixesTheDurationWithInWhenDetailed() =>
        Assert.Equal(" in 2.0s", Describe.TimedSuffix(TimeSpan.FromSeconds(2), new ReportContext(null!, null!, Detailed: true)));
}
