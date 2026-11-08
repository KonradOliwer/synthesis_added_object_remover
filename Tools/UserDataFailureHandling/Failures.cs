namespace AddedObjectRemover;

/// <summary>
/// Handles failures of the user's data, which are caught only where one independent part of the run is processed.
/// Known user-data problems with a recorded kind of their own are caught separately.
/// </summary>
public static class Failures
{
    /// <summary>
    /// Whether the failure comes from the user's data, so the run may carry on without the part that threw: a file that
    /// cannot be read (including one wrapped by parallel work) or data a library found malformed. Any other exception is
    /// a bug of ours or of a library and ends the run.
    /// </summary>
    public static bool IsRecoverable(Exception ex) =>
        ex is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(IsRecoverable)
            : ex is IOException or UnauthorizedAccessException or UserDataException;

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

    /// <summary>Runs one part of the run; if it throws a failure of the user's data, gives the result of the part not having run, built from the caught failure. Any other exception ends the run.</summary>
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
