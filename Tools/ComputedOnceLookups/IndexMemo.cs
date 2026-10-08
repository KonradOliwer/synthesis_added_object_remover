namespace AddedObjectRemover;

/// <summary>
/// One value per position of a fixed-size list, computed on first use without locks. Two threads
/// may compute the same position at the same time, so the computation must give the same value
/// every time. The value is written before its state is published with an interlocked store and
/// read only after a load sees the state.
/// </summary>
public sealed class IndexMemo<T>(int count) where T : struct
{
    private const int NotComputed = 0;
    private const int Computed = 1;

    private readonly T[] _values = new T[count];
    private readonly int[] _state = new int[count];

    public T GetOrCompute(int index, Func<int, T> compute)
    {
        if (Volatile.Read(ref _state[index]) == Computed) return _values[index];

        var value = compute(index);
        _values[index] = value;
        Interlocked.CompareExchange(ref _state[index], Computed, NotComputed);
        return value;
    }
}
