namespace AddedObjectRemover;

/// <summary>
/// A count that threads can add to at the same time; the total never depends on timing. It is an
/// int: its users count files, records and problems, far fewer than two billion.
/// </summary>
public sealed class AtomicCounter
{
    private int _value;

    public int Value => Volatile.Read(ref _value);

    /// <returns>The count after adding one; every caller gets a different number.</returns>
    public int Increment() => Interlocked.Increment(ref _value);
}
