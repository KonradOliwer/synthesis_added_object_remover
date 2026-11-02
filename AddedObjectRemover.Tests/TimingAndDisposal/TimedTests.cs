namespace AddedObjectRemover.Tests.TimingAndDisposal;

public class TimedTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    [Fact]
    public void RunReturnsTheResultAndTheTimeBetweenTheTwoClockReadings()
    {
        var now = TimeSpan.Zero;
        var readings = 0;
        TimeSpan Clock() => now += Tick * ++readings;

        var (result, time) = Timed.Run(Clock, () => 42);

        Assert.Equal(42, result);
        Assert.Equal(Tick * 2, time);
        Assert.Equal(2, readings);
    }

    [Fact]
    public void RunOfActionRunsTheWorkBetweenTheTwoClockReadingsAndReturnsTheTime()
    {
        var log = new List<string>();
        var now = TimeSpan.Zero;
        TimeSpan Clock()
        {
            log.Add("clock");
            return now += Tick;
        }

        var time = Timed.Run(Clock, () => log.Add("work"));

        Assert.Equal(["clock", "work", "clock"], log);
        Assert.Equal(Tick, time);
    }
}
