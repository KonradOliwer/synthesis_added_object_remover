namespace AddedObjectRemover;

/// <summary>
/// Handles unexpected failures, which are caught only where one independent part of the run is processed.
/// Known user-data problems are caught separately, each recorded as a problem of its own kind.
/// </summary>
public static class Failures
{
    /// <summary>
    /// Whether the run may carry on without the part that threw. OutOfMemory is deliberately excluded: it cannot be
    /// told apart from real memory pressure, so skipping a part would make the results depend on memory.
    /// That includes one wrapped by parallel work.
    /// </summary>
    public static bool IsRecoverable(Exception ex) =>
        ex is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.All(IsRecoverable)
            : ex is not OutOfMemoryException;

    /// <remarks>Parallel work wraps its failures; the distinct inner ones are listed in a fixed order, so the text does not depend on which worker failed first.</remarks>
    public static string DescribeFailure(Exception ex) =>
        ex is AggregateException aggregate
            ? string.Join("; ", aggregate.Flatten().InnerExceptions.Select(DescribeFailure).Distinct().Order(StringComparer.Ordinal))
            : $"{ex.GetType().Name}: {string.Join(' ', ex.Message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.')}";

    public static CaughtFailure Catch(Exception ex) => new(DescribeFailure(ex), DetailsOf(ex));

    /// <remarks>Of the failures parallel work wraps, the one whose description sorts first, so the text does not depend on which worker failed first.</remarks>
    private static string DetailsOf(Exception ex) =>
        ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.OrderBy(DescribeFailure, StringComparer.Ordinal).FirstOrDefault() is { } first
            ? DetailsOf(first)
            : ex.ToString();

    /// <summary>Runs one part of the run; if it throws unexpectedly, gives the result of the part not having run, built from the caught failure.</summary>
    /// <param name="stopped">Builds that result from <see cref="Catch"/>; only called when the part threw.</param>
    public static T Attempt<T>(Func<T> work, Func<CaughtFailure, T> stopped)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return stopped(Catch(ex));
        }
    }
}

/// <summary>An exception caught where one part of the run is processed.</summary>
/// <param name="Description">Its type and message, as <see cref="Failures.DescribeFailure"/> gives them.</param>
/// <param name="Details">Its full text with the stack trace and inner exceptions.</param>
public sealed record CaughtFailure(string Description, string Details);
