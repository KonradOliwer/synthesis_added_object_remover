using System.Diagnostics;

namespace AddedObjectRemover;

internal static class Timing
{
    public static (T Result, TimeSpan Elapsed) Measure<T>(Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        var result = action();
        return (result, timer.Elapsed);
    }

    public static TimeSpan Measure(Action action)
    {
        var timer = Stopwatch.StartNew();
        action();
        return timer.Elapsed;
    }
}
