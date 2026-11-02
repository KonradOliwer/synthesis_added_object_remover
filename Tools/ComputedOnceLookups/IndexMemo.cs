namespace AddedObjectRemover;

/// <summary>
/// One value per position of a fixed-size list, computed on first use by exactly one thread; threads asking for the same
/// position at the same time wait for it. Thread-safe. A computation that throws is passed on to its caller, and the next
/// caller of that position computes again.
/// </summary>
public sealed class IndexMemo<T>(int count) where T : struct
{
    private readonly T[] _values = new T[count];
    private readonly bool[] _computed = new bool[count];
    private readonly object?[] _locks = new object?[count];

    public T GetOrCompute(int index, Func<int, T> compute) =>
        LazyInitializer.EnsureInitialized(ref _values[index], ref _computed[index], ref _locks[index], () => compute(index));
}
