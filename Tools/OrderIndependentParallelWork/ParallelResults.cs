namespace AddedObjectRemover;

/// <summary>Reads the per-index results of a parallel map in index order, so the outcome never depends on how the work was scheduled.</summary>
public static class ParallelResults
{
    /// <returns>The results that are not null, in index order.</returns>
    public static List<T> Compact<T>(IReadOnlyList<T?> results)
        where T : class
    {
        var kept = new List<T>();
        foreach (var result in results)
        {
            if (result is not null) kept.Add(result);
        }
        return kept;
    }

    /// <returns>The results that are not null, in index order.</returns>
    public static List<T> Compact<T>(IReadOnlyList<T?> results)
        where T : struct
    {
        var kept = new List<T>();
        foreach (var result in results)
        {
            if (result is { } value) kept.Add(value);
        }
        return kept;
    }

    /// <returns>The indices, ascending, of the results the predicate accepts.</returns>
    public static List<int> IndicesWhere<T>(IReadOnlyList<T> results, Func<T, bool> predicate)
    {
        var indices = new List<int>();
        for (var i = 0; i < results.Count; i++)
        {
            if (predicate(results[i])) indices.Add(i);
        }
        return indices;
    }
}
