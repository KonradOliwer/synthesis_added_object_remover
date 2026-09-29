using System.Collections.Immutable;
using System.Diagnostics;

namespace AddedObjectRemover;

/// <summary>Records how long each timed phase took, for the detailed log.</summary>
internal sealed class PhaseClock : IPhaseTimer
{
    private readonly Dictionary<TimedPhase, List<TimeSpan>> _samples = [];
    private readonly Lock _samplesLock = new();

    public T Time<T>(TimedPhase phase, Func<T> work)
    {
        var timer = Stopwatch.StartNew();
        var result = work();
        Record(phase, timer.Elapsed);
        return result;
    }

    public void Time(TimedPhase phase, Action work)
    {
        var timer = Stopwatch.StartNew();
        work();
        Record(phase, timer.Elapsed);
    }

    /// <summary>The times recorded so far.</summary>
    public PhaseTimes Times()
    {
        lock (_samplesLock) return new PhaseTimes(_samples.ToImmutableDictionary(entry => entry.Key, entry => entry.Value.ToImmutableArray()));
    }

    private void Record(TimedPhase phase, TimeSpan elapsed)
    {
        lock (_samplesLock)
        {
            if (!_samples.TryGetValue(phase, out var samples)) _samples[phase] = samples = [];
            samples.Add(elapsed);
        }
    }
}
