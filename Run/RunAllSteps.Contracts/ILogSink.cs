using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>Where a run's log sections go; the only way the composition prints.</summary>
public interface ILogSink
{
    void Print(LogSection section);

    /// <summary>Runs <paramref name="work"/>, then prints, in order, the sections built from its result and how long it took.</summary>
    T Timed<T>(Func<T> work, Func<T, TimeSpan, ImmutableArray<LogSection>> sections);
}
