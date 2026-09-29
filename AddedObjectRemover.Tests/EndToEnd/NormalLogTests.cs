namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Without the detailed log, no timing, thread count or performance statistic reaches the log; checked before any masking.</summary>
[Collection(ConsoleOutputCollection.Name)]
public class NormalLogTests
{
    private const int Workers = 8;

    [Fact]
    public void UnmaskedLogHasNoPerformanceLinesThreadCountOrDurations()
    {
        var log = PipelineRun.Execute(SettingsVariants.Of(SettingsVariants.TouchWithLeftoversAndRelocationNormalLog), Workers).UnmaskedLog;

        Assert.DoesNotContain(log, line => OutputMasks.PerformanceLinePrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.DoesNotContain(log, line => OutputMasks.ThreadCount().IsMatch(line));
        Assert.DoesNotContain(log, line => OutputMasks.Duration().IsMatch(line));
    }
}
