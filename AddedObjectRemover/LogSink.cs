using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Run.RunAllSteps.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover;

/// <summary>Prints a run's log sections to the console.</summary>
internal sealed class ConsoleLogSink : ILogSink
{
    public void Print(LogSection section)
    {
        foreach (var line in section.Lines) Console.WriteLine(line);
    }

    public T Timed<T>(Func<T> work, Func<T, TimeSpan, ImmutableArray<LogSection>> sections)
    {
        var timer = Stopwatch.StartNew();
        var result = work();
        foreach (var section in sections(result, timer.Elapsed)) Print(section);
        return result;
    }
}
