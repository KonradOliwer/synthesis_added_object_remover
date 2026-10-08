namespace AddedObjectRemover;

/// <summary>The file-system failures a run tolerates; anything else propagates.</summary>
public static class FileFailures
{
    public static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException;

    /// <summary>Runs the action; an expected failure is caught and reported with the caller's message.</summary>
    /// <param name="failureMessage">The caller's wording for the failure.</param>
    /// <returns>The failure message; null when the action succeeded.</returns>
    public static string? Guard(Action action, Func<Exception, string> failureMessage)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return failureMessage(exception);
        }
    }
}
