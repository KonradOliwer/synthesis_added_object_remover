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

    /// <summary>The first <paramref name="limit"/> items of an already ranked list, each as <paramref name="format"/> writes it.</summary>
    public static string TopN<T>(IEnumerable<T> ranked, int limit, Func<T, string> format, string separator) =>
        string.Join(separator, ranked.Take(limit).Select(format));
}
