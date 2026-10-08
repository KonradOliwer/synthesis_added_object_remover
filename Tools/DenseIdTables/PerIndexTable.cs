namespace AddedObjectRemover;

/// <summary>One value per dense index, for some of the indices; lookup is an array read.</summary>
public sealed class PerIndexTable<T>
{
    private readonly T[] _values;
    private readonly bool[] _present;

    private PerIndexTable(T[] values, bool[] present)
    {
        _values = values;
        _present = present;
    }

    /// <exception cref="ArgumentException">Two items have the same index.</exception>
    public static PerIndexTable<T> From(IEnumerable<T> items, Func<T, int> indexOf) => From(items, indexOf, static item => item);

    /// <summary>The value of each item, at the item's index.</summary>
    /// <exception cref="ArgumentException">Two items have the same index.</exception>
    public static PerIndexTable<T> From<TItem>(IEnumerable<TItem> items, Func<TItem, int> indexOf, Func<TItem, T> valueOf)
    {
        var pairs = items.Select(item => (Index: indexOf(item), Value: valueOf(item))).ToList();
        var size = pairs.Count == 0 ? 0 : pairs.Max(pair => pair.Index) + 1;
        var values = new T[size];
        var present = new bool[size];
        foreach (var (index, value) in pairs)
        {
            if (present[index]) throw new ArgumentException($"Index {index} has more than one item.", nameof(items));
            values[index] = value;
            present[index] = true;
        }
        return new PerIndexTable<T>(values, present);
    }

    public bool Has(int index) => (uint)index < (uint)_present.Length && _present[index];

    public T Get(int index) =>
        Has(index) ? _values[index] : throw new KeyNotFoundException($"No item has index {index}.");

    public bool TryGet(int index, out T value)
    {
        var has = Has(index);
        value = has ? _values[index] : default!;
        return has;
    }
}
