namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The check every allow list of the architecture tests ends with: no entry is left that the code no longer needs.</summary>
internal static class AllowListAssert
{
    /// <param name="stale">The entries the code no longer needs, one line each.</param>
    /// <param name="message">What is wrong with them, printed before the entries.</param>
    public static void NoneStale(IReadOnlyCollection<string> stale, string message) =>
        Assert.True(stale.Count == 0, $"{message}:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
}
