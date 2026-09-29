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
