namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Keeps every printed section, so tests can look at what a run printed and in which order.</summary>
internal sealed class RecordingLogSink : ILogSink
{
    public List<LogSection> Sections { get; } = [];

    /// <summary>Called with each section as it is printed, so a test can switch a fault on or off at a point of the run.</summary>
    public Action<LogSection>? OnPrint { get; init; }

    /// <summary>Thrown when the work given to <c>Measured</c> has finished, as a failure that comes after the work's own effects.</summary>
    public Exception? FailAfterMeasuredWork { get; init; }

    public void Print(LogSection section)
    {
        Sections.Add(section);
        OnPrint?.Invoke(section);
    }

    public T Timed<T>(Func<T> work, Func<T, TimeSpan, LogSection> section)
    {
        var result = work();
        Print(section(result, TimeSpan.Zero));
        return result;
    }

    public T Measured<T>(Func<T> work, out TimeSpan elapsed)
    {
        elapsed = TimeSpan.Zero;
        var result = work();
        if (FailAfterMeasuredWork is { } failure) throw failure;
        return result;
    }
}
