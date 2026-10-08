using System.Collections.Concurrent;

namespace AddedObjectRemover;

/// <summary>How a <see cref="ComputedOncePerKey{TKey,TValue}"/> treats threads that ask for the same missing key at the same time.</summary>
public enum Publication
{
    /// <summary>Every asking thread may compute; the first finished value is kept and the others are discarded. For small values.</summary>
    FirstWriteWins,

    /// <summary>One thread computes while the others wait for its value. For large values.</summary>
    BuiltOnce,
}

/// <summary>
/// Thread-safe cache that keeps one value per key. A value must depend only on its key, so
/// the result never depends on which thread computed it. A computation that throws is passed on
/// to the caller; with <see cref="Publication.BuiltOnce"/> it is also what every later caller of
/// that key gets, with <see cref="Publication.FirstWriteWins"/> the next caller computes again.
/// </summary>
public sealed class ComputedOncePerKey<TKey, TValue>(Publication publication, IEqualityComparer<TKey> comparer)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _entries = new(comparer);

    public TValue Get(TKey key, Func<TValue> compute) =>
        _entries.GetOrAdd(key, _ => new Lazy<TValue>(compute, ThreadSafetyOf(publication))).Value;

    /// <summary>The values computed so far, in no particular order.</summary>
    public List<TValue> Contents() =>
        _entries.Values.Where(entry => entry.IsValueCreated).Select(entry => entry.Value).ToList();

    private static LazyThreadSafetyMode ThreadSafetyOf(Publication publication) => publication switch
    {
        Publication.FirstWriteWins => LazyThreadSafetyMode.PublicationOnly,
        Publication.BuiltOnce => LazyThreadSafetyMode.ExecutionAndPublication,
        _ => throw new ArgumentOutOfRangeException(nameof(publication), publication, null),
    };
}
