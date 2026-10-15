using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Keeps every printed section, so tests can look at what a run printed and in which order.</summary>
internal sealed class RecordingLogSink : ILogSink
{
    public List<LogSection> Sections { get; } = [];

    /// <summary>Called with each section as it is printed, so a test can switch a fault on or off at a point of the run.</summary>
    public Action<LogSection>? OnPrint { get; init; }

    public void Print(LogSection section)
    {
        Sections.Add(section);
        OnPrint?.Invoke(section);
    }

    public T Timed<T>(Func<T> work, Func<T, TimeSpan, ImmutableArray<LogSection>> sections)
    {
        var result = work();
        foreach (var section in sections(result, TimeSpan.Zero)) Print(section);
        return result;
    }
}
