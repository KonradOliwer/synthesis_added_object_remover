namespace AddedObjectRemover;

/// <summary>A value computed on first use by exactly one thread; threads asking at the same time wait for it. Thread-safe.</summary>
public sealed class ComputedOnce<T>(Func<T> compute)
{
    private readonly Lazy<T> _lazy = new(compute, LazyThreadSafetyMode.ExecutionAndPublication);

    public T Value => _lazy.Value;

    public bool IsComputed => _lazy.IsValueCreated;
}
