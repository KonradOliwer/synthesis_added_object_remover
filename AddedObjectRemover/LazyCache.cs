using System.Collections.Concurrent;

namespace AddedObjectRemover;

/// <summary>Thread-safe cache that creates each value exactly once, even when many threads ask for it at the same time.</summary>
internal sealed class LazyCache<TKey, TValue>(IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<TValue>> _entries = new(comparer);

    public TValue GetOrCreate(TKey key, Func<TValue> create) =>
        _entries.GetOrAdd(key, _ => new Lazy<TValue>(create, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
}
