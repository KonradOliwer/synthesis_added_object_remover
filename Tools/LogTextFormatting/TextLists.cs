namespace AddedObjectRemover;

/// <summary>Lists as log text. The caller supplies every separator and wording; nothing here has a default text.</summary>
public static class TextLists
{
    /// <summary>The items joined by <paramref name="separator"/>, or <paramref name="emptyText"/> when there are none.</summary>
    public static string JoinOr(IEnumerable<string> items, string separator, string emptyText)
    {
        var list = items.ToList();
        return list.Count == 0 ? emptyText : string.Join(separator, list);
    }

    /// <summary>The first <paramref name="limit"/> items, then <paramref name="moreText"/> of the number left out when there are more.</summary>
    public static string Capped(IReadOnlyList<string> items, int limit, string separator, Func<int, string> moreText)
    {
        var shown = items.Take(limit);
        return items.Count > limit ? string.Join(separator, shown.Append(moreText(items.Count - limit))) : string.Join(separator, shown);
    }

    /// <summary>One "N name" item per entry, the number written with digit grouping.</summary>
    public static IEnumerable<string> Counts<TName>(IEnumerable<KeyValuePair<TName, int>> countsByName) =>
        countsByName.Select(entry => $"{TextFormat.Count(entry.Value)} {entry.Key}");

    /// <summary>The "N name" item of each entry, joined by <paramref name="separator"/>.</summary>
    public static string JoinCounts<T>(IEnumerable<T> entries, Func<T, string> nameOf, Func<T, int> countOf, string separator) =>
        string.Join(separator, Counts(entries.Select(entry => KeyValuePair.Create(nameOf(entry), countOf(entry)))));

    /// <summary>"Heading: item, item." with the items joined by <paramref name="separator"/>; nothing when there is no item.</summary>
    public static IEnumerable<string> Headed(string heading, IEnumerable<string> items, string separator)
    {
        var list = items.ToList();
        if (list.Count > 0) yield return $"{heading}: {string.Join(separator, list)}.";
    }

    /// <summary>The name of each flag set in <paramref name="flags"/>, in the order of the enum and without its zero value, joined by <paramref name="separator"/>.</summary>
    public static string JoinSetFlags<TFlags>(TFlags flags, Func<TFlags, string> nameOf, string separator)
        where TFlags : struct, Enum =>
        string.Join(separator, Enum.GetValues<TFlags>().Where(flag => !EqualityComparer<TFlags>.Default.Equals(flag, default) && flags.HasFlag(flag)).Select(nameOf));

    /// <summary>The first <paramref name="limit"/> items of an already ranked list, each as <paramref name="format"/> writes it.</summary>
    public static string TopN<T>(IEnumerable<T> ranked, int limit, Func<T, string> format, string separator) =>
        string.Join(separator, ranked.Take(limit).Select(format));
}
