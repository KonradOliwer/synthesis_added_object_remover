namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The check every allow list of the architecture tests ends with: no entry is left that the code no longer needs.</summary>
internal static class AllowListAssert
{
    /// <param name="stale">The entries the code no longer needs, one line each.</param>
    /// <param name="message">What is wrong with them, printed before the entries.</param>
    public static void NoneStale(IReadOnlyCollection<string> stale, string message) =>
        Assert.True(stale.Count == 0, $"{message}:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");

    /// <summary>
    /// An allow list of places with how many finds each may hold: a place holds no more than its count, and none holds fewer.
    /// </summary>
    /// <param name="describe">One line for a place and the count found or allowed there.</param>
    public static void ExactCounts<TPlace>(
        IReadOnlyDictionary<TPlace, int> found,
        IReadOnlyDictionary<TPlace, int> allowed,
        Func<TPlace, int, string> describe,
        string notAllowedMessage,
        string staleMessage)
        where TPlace : notnull
    {
        var notAllowed = found.Where(place => place.Value > allowed.GetValueOrDefault(place.Key)).Select(place => describe(place.Key, place.Value)).Order(StringComparer.Ordinal).ToList();
        var stale = allowed.Where(place => place.Value > found.GetValueOrDefault(place.Key)).Select(place => describe(place.Key, place.Value)).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"{notAllowedMessage}:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        NoneStale(stale, staleMessage);
    }
}
