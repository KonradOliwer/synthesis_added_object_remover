using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Parts of steps whose time the detailed log reports.</summary>
internal enum TimedPhase
{
    TouchSetup,
    TouchBroadPhase,
    TouchNarrowPhase,
    TouchDiagnosticsEdges,
    AnchoringSetup,
    AnchoringTouchSearch,
    AnchoringContactPoints,
    RivalBoundsBuild,
    SolidBoundsBuild,
    ShapeZoneIndexBuild,
    ShapeZoneSearch,
}

/// <summary>
/// Lets a step have its phases timed without reading the clock itself, so no time ever reaches a
/// step's result. Thread-safe; a phase may be timed many times, also concurrently. Phases may nest,
/// so the time of an outer phase includes the inner ones.
/// </summary>
internal interface IPhaseTimer
{
    T Time<T>(TimedPhase phase, Func<T> work);

    void Time(TimedPhase phase, Action work);
}

/// <summary>Runs the work without timing it.</summary>
internal sealed class UntimedPhases : IPhaseTimer
{
    public static UntimedPhases Instance { get; } = new();

    private UntimedPhases()
    {
    }

    public T Time<T>(TimedPhase phase, Func<T> work) => work();

    public void Time(TimedPhase phase, Action work) => work();
}

/// <summary>The times recorded for the timed phases, as data for the detailed log.</summary>
/// <param name="ByPhase">Every time a phase took, in the order the timings ended.</param>
internal sealed record PhaseTimes(ImmutableDictionary<TimedPhase, ImmutableArray<TimeSpan>> ByPhase)
{
    public static PhaseTimes None { get; } = new(ImmutableDictionary<TimedPhase, ImmutableArray<TimeSpan>>.Empty);

    public ImmutableArray<TimeSpan> Samples(TimedPhase phase) => ByPhase.GetValueOrDefault(phase, []);

    public TimeSpan Total(TimedPhase phase) => Samples(phase).Aggregate(TimeSpan.Zero, (total, sample) => total + sample);
}
