using System.Collections.Immutable;
using System.Diagnostics;

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

    /// <returns>
    /// One group per distinct key, in the order the keys first appear, holding the items of every group with that key in ascending
    /// order; the group's key is the first one seen.
    /// </returns>
    /// <remarks>Throws when an item is in more than one group.</remarks>
    public static List<KeyValuePair<TKey, ImmutableArray<T>>> MergeGroupsWithEqualKeys<TGroup, T, TKey>(
        IEnumerable<TGroup> groups, Func<TGroup, IEnumerable<T>> itemsOf, Func<TGroup, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var all = groups.ToList();
        RequireEachItemInOneGroup(all.Select(itemsOf));
        return
        [
            .. IndicesByKey(all, keyOf, comparer)
                .Select(merged => KeyValuePair.Create(merged.Key, merged.Indices.SelectMany(index => itemsOf(all[index])).Order().ToImmutableArray())),
        ];
    }

    /// <returns>The value of the group each item stands in.</returns>
    /// <remarks>Throws when an item is in more than one group.</remarks>
    public static ImmutableDictionary<T, TValue> ValueByItem<TGroup, T, TValue>(
        IEnumerable<TGroup> groups, Func<TGroup, IEnumerable<T>> itemsOf, Func<TGroup, TValue> valueOf)
        where T : notnull
    {
        var all = groups.ToList();
        RequireEachItemInOneGroup(all.Select(itemsOf));
        return all.SelectMany(group => itemsOf(group).Select(item => KeyValuePair.Create(item, valueOf(group)))).ToImmutableDictionary();
    }

    public static void RequireEachItemInOneGroup<T>(IEnumerable<IEnumerable<T>> groups)
    {
        HashSet<T> seen = [];
        foreach (var item in groups.SelectMany(group => group))
        {
            if (!seen.Add(item)) throw new UnreachableException($"Item {item} is in more than one group.");
        }
    }

    /// <returns>One entry per distinct key with its number of items, in the order the keys first appear; the entry's key is the first one seen.</returns>
    public static List<KeyValuePair<TKey, int>> CountBy<T, TKey>(
        IEnumerable<T> items, Func<T, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var keys = new List<TKey>();
        var counts = new List<int>();
        var positionOf = new Dictionary<TKey, int>(comparer);
        foreach (var item in items)
        {
            var key = keyOf(item);
            if (!positionOf.TryGetValue(key, out var position))
            {
                position = keys.Count;
                positionOf[key] = position;
                keys.Add(key);
                counts.Add(0);
            }
            counts[position]++;
        }
        return [.. keys.Zip(counts, (key, count) => KeyValuePair.Create(key, count))];
    }

    /// <returns>Each item's position in the list by the item's key.</returns>
    /// <remarks>Throws when two items have the same key.</remarks>
    public static Dictionary<TKey, int> PositionByKey<T, TKey>(IEnumerable<T> items, Func<T, TKey> keyOf)
        where TKey : notnull =>
        items.Select((item, position) => (Key: keyOf(item), Position: position)).ToDictionary(entry => entry.Key, entry => entry.Position);

    /// <returns>The counts with the highest first; equal counts are ordered by key, and keys the comparer treats as equal keep their input order.</returns>
    public static List<KeyValuePair<TKey, int>> Rank<TKey>(
        IEnumerable<KeyValuePair<TKey, int>> counts, IComparer<TKey> keyComparer) =>
        counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, keyComparer).ToList();

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
