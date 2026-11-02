namespace AddedObjectRemover;

public static class Timed
{
    /// <param name="clock">Read once before and once after <paramref name="work"/>.</param>
    /// <returns>What <paramref name="work"/> returns, and the time between the two readings.</returns>
    public static (T Result, TimeSpan Time) Run<T>(Func<TimeSpan> clock, Func<T> work)
    {
        var start = clock();
        var result = work();
        return (result, clock() - start);
    }

    /// <param name="clock">Read once before and once after <paramref name="work"/>.</param>
    /// <returns>The time between the two readings.</returns>
    public static TimeSpan Run(Func<TimeSpan> clock, Action work)
    {
        var start = clock();
        work();
        return clock() - start;
    }
}
