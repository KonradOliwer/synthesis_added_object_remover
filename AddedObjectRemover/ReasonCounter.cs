using System.Collections.Concurrent;

namespace AddedObjectRemover;

/// <summary>Thread-safe count of occurrences per reason, e.g. mesh failures.</summary>
internal sealed class ReasonCounter
{
    private readonly ConcurrentDictionary<string, int> _byReason = new(StringComparer.Ordinal);

    public void Add(string reason) => _byReason.AddOrUpdate(reason, 1, (_, count) => count + 1);

    /// <summary>Most frequent first, ties by reason.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Snapshot() =>
        _byReason.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
}
