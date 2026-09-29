namespace AddedObjectRemover.Tests.Determinism;

public class PhaseTimerTests
{
    private const int ConcurrentTimings = 200;
    private const int Answer = 42;

    [Fact]
    public void UntimedPhasesOnlyRunTheWork()
    {
        var ran = false;

        UntimedPhases.Instance.Time(TimedPhase.TouchBroadPhase, () => { ran = true; });

        Assert.True(ran);
        Assert.Equal(Answer, UntimedPhases.Instance.Time(TimedPhase.TouchBroadPhase, () => Answer));
    }

    [Fact]
    public void TheClockRecordsEachTimingOfAPhase()
    {
        var clock = new PhaseClock();

        var result = clock.Time(TimedPhase.TouchNarrowPhase, () => Answer);
        clock.Time(TimedPhase.TouchNarrowPhase, () => { });

        Assert.Equal(Answer, result);
        var samples = clock.Times().Samples(TimedPhase.TouchNarrowPhase);
        Assert.Equal(2, samples.Length);
        Assert.Equal(samples[0] + samples[1], clock.Times().Total(TimedPhase.TouchNarrowPhase));
        Assert.Empty(clock.Times().Samples(TimedPhase.TouchSetup));
        Assert.Equal(TimeSpan.Zero, clock.Times().Total(TimedPhase.TouchSetup));
    }

    [Fact]
    public void NestedPhasesAreBothRecorded()
    {
        var clock = new PhaseClock();

        clock.Time(TimedPhase.TouchSetup, () => clock.Time(TimedPhase.RivalBoundsBuild, () => { }));
        clock.Time(TimedPhase.TouchSetup, () => clock.Time(TimedPhase.TouchSetup, () => { }));

        var outer = clock.Times().Samples(TimedPhase.TouchSetup);
        Assert.Equal(3, outer.Length);
        Assert.Single(clock.Times().Samples(TimedPhase.RivalBoundsBuild));
        Assert.True(outer[0] >= clock.Times().Samples(TimedPhase.RivalBoundsBuild)[0]);
    }

    [Fact]
    public void WorkThatThrowsRecordsNoSample()
    {
        var clock = new PhaseClock();

        Assert.Throws<InvalidOperationException>(() => clock.Time(TimedPhase.TouchSetup, () => throw new InvalidOperationException()));

        Assert.Empty(clock.Times().Samples(TimedPhase.TouchSetup));
    }

    [Fact]
    public void TheClockKeepsConcurrentTimings()
    {
        var clock = new PhaseClock();

        Parallel.For(0, ConcurrentTimings, _ => clock.Time(TimedPhase.RivalBoundsBuild, () => { }));

        Assert.Equal(ConcurrentTimings, clock.Times().Samples(TimedPhase.RivalBoundsBuild).Length);
    }
}
