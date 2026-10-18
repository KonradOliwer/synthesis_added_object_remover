namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The report writer alone decides whether report files are written, and so whether the report-file details are computed.</summary>
public class ReportFilesDecisionTests
{
    [Theory]
    [InlineData(false, StepOutcome.SwitchedOff)]
    [InlineData(true, StepOutcome.Performed)]
    public void TheReportFileDetailsAreComputedOnlyWhenTheWriterPreparedReportFiles(bool detailedLogAndReports, StepOutcome expected)
    {
        var settings = SettingsVariants.Of(SettingsVariants.Support);
        settings.DetailedLogAndReports = detailedLogAndReports;

        var done = (RunResult.Done)FixtureRun.Run(
            settings,
            workers: 2,
            new RecordingLogSink(),
            (dataFolder, release, listedPlugins) => new MeshFilesFactory(dataFolder, release, listedPlugins));

        Assert.Equal(expected, done.Outcome.RestingObjects.ReportFileDetails.Outcome);
    }
}
