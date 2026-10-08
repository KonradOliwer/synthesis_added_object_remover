using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover;

/// <summary>Where a run's log sections go; the only way the composition prints.</summary>
public interface ILogSink
{
    void Print(LogSection section);

    /// <summary>Runs <paramref name="work"/>, then prints the section built from its result and how long it took.</summary>
    T Timed<T>(Func<T> work, Func<T, TimeSpan, LogSection> section);

    /// <summary>Runs <paramref name="work"/> and says how long it took, for a section built later.</summary>
    T Measured<T>(Func<T> work, out TimeSpan elapsed);
}

/// <summary>Prints a run's log sections to the console.</summary>
internal sealed class ConsoleLogSink : ILogSink
{
    public void Print(LogSection section)
    {
        foreach (var line in section.Lines) Console.WriteLine(line);
    }

    public T Timed<T>(Func<T> work, Func<T, TimeSpan, LogSection> section)
    {
        var timer = Stopwatch.StartNew();
        var result = work();
        Print(section(result, timer.Elapsed));
        return result;
    }

    public T Measured<T>(Func<T> work, out TimeSpan elapsed)
    {
        var timer = Stopwatch.StartNew();
        var result = work();
        elapsed = timer.Elapsed;
        return result;
    }
}
