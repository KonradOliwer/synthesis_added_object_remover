namespace AddedObjectRemover;

/// <param name="Indices">Positions in the grouped list, ascending.</param>
public readonly record struct IndexGroup<TKey>(TKey Key, List<int> Indices);

/// <summary>Grouping that keeps the input order, so results never depend on hashing or timing.</summary>
public static class KeyedGroups
{
    /// <returns>One group per distinct key, in the order the keys first appear; the group's key is the first one seen.</returns>
    public static List<IndexGroup<TKey>> IndicesByKey<T, TKey>(
        IReadOnlyList<T> items, Func<T, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var groups = new List<IndexGroup<TKey>>();
        var groupOf = new Dictionary<TKey, int>(comparer);
        for (var i = 0; i < items.Count; i++)
        {
            var key = keyOf(items[i]);
            if (!groupOf.TryGetValue(key, out var group))
            {
                group = groups.Count;
                groupOf[key] = group;
                groups.Add(new IndexGroup<TKey>(key, []));
            }
            groups[group].Indices.Add(i);
        }
        return groups;
    }

    /// <returns>One entry per distinct key with its number of items, in the order the keys first appear; the entry's key is the first one seen.</returns>
    public static List<KeyValuePair<TKey, int>> CountBy<T, TKey>(
        IEnumerable<T> items, Func<T, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var counts = new List<KeyValuePair<TKey, int>>();
        var slotOf = new Dictionary<TKey, int>(comparer);
        foreach (var item in items)
        {
            var key = keyOf(item);
            if (slotOf.TryGetValue(key, out var slot))
            {
                counts[slot] = KeyValuePair.Create(counts[slot].Key, counts[slot].Value + 1);
            }
            else
            {
                slotOf[key] = counts.Count;
                counts.Add(KeyValuePair.Create(key, 1));
            }
        }
        return counts;
    }

    /// <returns>The counts with the highest first; equal counts are ordered by key, and keys the comparer treats as equal keep their input order.</returns>
    public static List<KeyValuePair<TKey, int>> Rank<TKey>(
        IEnumerable<KeyValuePair<TKey, int>> counts, IComparer<TKey> keyComparer) =>
        counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, keyComparer).ToList();

    /// <returns>The first item of each distinct key, in input order.</returns>
    public static List<T> DistinctBy<T, TKey>(
        IEnumerable<T> items, Func<T, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>(comparer);
        return items.Where(item => seen.Add(keyOf(item))).ToList();
    }

    /// <returns>The list stored under the key; an empty one is stored first when the key has none.</returns>
    public static List<T> GetOrAddList<TKey, T>(Dictionary<TKey, List<T>> lists, TKey key)
        where TKey : notnull
    {
        if (!lists.TryGetValue(key, out var list))
        {
            list = [];
            lists[key] = list;
        }
        return list;
    }

    /// <returns>The first item whose value is the greatest under the comparer; later items with an equal value do not replace it.</returns>
    /// <remarks>Throws for an empty list.</remarks>
    public static T ArgMaxFirstWins<T, TValue>(IReadOnlyList<T> items, Func<T, TValue> valueOf, IComparer<TValue> valueComparer)
    {
        var best = items[0];
        var bestValue = valueOf(best);
        for (var i = 1; i < items.Count; i++)
        {
            var value = valueOf(items[i]);
            if (valueComparer.Compare(value, bestValue) <= 0) continue;
            best = items[i];
            bestValue = value;
        }
        return best;
    }
}
