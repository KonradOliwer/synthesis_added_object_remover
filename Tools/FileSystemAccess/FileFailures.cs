namespace AddedObjectRemover;

/// <summary>The file-system failures a run tolerates; anything else propagates.</summary>
public static class FileFailures
{
    public static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException;

    /// <summary>Runs the action; an expected failure is caught and reported as the caller's failure value.</summary>
    /// <param name="createFailure">The caller's value for the failure.</param>
    /// <returns>The failure value; null when the action succeeded.</returns>
    public static TFailure? Guard<TFailure>(Action action, Func<Exception, TFailure> createFailure)
        where TFailure : class
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return createFailure(exception);
        }
    }
}
