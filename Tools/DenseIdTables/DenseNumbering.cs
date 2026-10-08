using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>Gives items dense ids by position in a sort order.</summary>
public static class DenseNumbering
{
    /// <returns>The items sorted by key (equal keys keep their input order), each given the id <paramref name="firstId"/> plus its position.</returns>
    public static ImmutableArray<T> InOrder<T, TKey>(
        IEnumerable<T> items, Func<T, TKey> keyOf, IComparer<TKey> keyComparer, int firstId, Func<T, int, T> withId) =>
        [.. items.OrderBy(keyOf, keyComparer).Select((item, position) => withId(item, firstId + position))];
}
