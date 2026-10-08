namespace AddedObjectRemover;

/// <summary>The state of one key; every field is guarded by the store's lock.</summary>
public sealed class StoreSlot<T> where T : class
{
    public Lazy<T?>? Value;
    public long LastUse;
    public int Users;
}

/// <summary>
/// Thread-safe store of one built value per key, handed out as leases. A value is built on its
/// first use by one thread while the others wait for it, and stays resident until the resident
/// size exceeds the maximum; then the least recently used values that no lease holds are dropped
/// together with their slots (and built again if asked for again). A key whose build gave nothing
/// keeps its slot, because it takes no size.
/// </summary>
/// <param name="build">Gives null when the key has no usable value; that is kept as the key's value.</param>
/// <param name="sizeOf">The size a resident value counts for.</param>
/// <param name="maxResidentSize">The resident size above which idle values are evicted.</param>
/// <param name="evictDownToSize">The resident size eviction stops at.</param>
/// <param name="comparer">Decides which keys are the same key.</param>
public sealed class EvictingLeasedStore<TKey, T>(
    Func<TKey, T?> build,
    Func<T, long> sizeOf,
    long maxResidentSize,
    long evictDownToSize,
    IEqualityComparer<TKey> comparer) : ILeaseOwner<T>
    where TKey : notnull
    where T : class
{
    private readonly Dictionary<TKey, StoreSlot<T>> _slots = new(comparer);
    private readonly Dictionary<TKey, StoreSlot<T>> _residentSlots = new(comparer);
    private readonly object _lock = new();

    private long _clock;
    private long _residentSize;

    /// <summary>
    /// The value stays resident at least until the returned lease is disposed. When the build
    /// throws, the exception passes through and the value is built again on the key's next use.
    /// </summary>
    public Lease<T> Acquire(TKey key)
    {
        var (slot, pending) = BeginUse(key);
        T? value;
        try
        {
            value = pending.Value;
        }
        catch
        {
            EndFailedUse(slot, pending);
            throw;
        }
        if (value != null && Volatile.Read(ref _residentSize) > maxResidentSize) EvictLeastRecentlyUsed();
        return new Lease<T>(value, this, slot);
    }

    void ILeaseOwner<T>.EndUse(StoreSlot<T> slot)
    {
        lock (_lock) slot.Users--;
    }

    private (StoreSlot<T> Slot, Lazy<T?> Pending) BeginUse(TKey key)
    {
        lock (_lock)
        {
            if (!_slots.TryGetValue(key, out var slot))
            {
                slot = new StoreSlot<T>();
                _slots[key] = slot;
            }
            slot.Value ??= new Lazy<T?>(() => BuildAndRegisterResident(key, slot), LazyThreadSafetyMode.ExecutionAndPublication);
            slot.Users++;
            slot.LastUse = ++_clock;
            return (slot, slot.Value);
        }
    }

    /// <summary>A lazy value keeps its exception, so the failed one is dropped (unless another use already replaced it).</summary>
    private void EndFailedUse(StoreSlot<T> slot, Lazy<T?> failed)
    {
        lock (_lock)
        {
            slot.Users--;
            if (slot.Value == failed) slot.Value = null;
        }
    }

    private T? BuildAndRegisterResident(TKey key, StoreSlot<T> slot)
    {
        var value = build(key);
        if (value != null) RegisterResident(key, slot, value);
        return value;
    }

    private void RegisterResident(TKey key, StoreSlot<T> slot, T value)
    {
        lock (_lock)
        {
            _residentSlots[key] = slot;
            _residentSize += sizeOf(value);
        }
    }

    private void EvictLeastRecentlyUsed()
    {
        lock (_lock)
        {
            if (_residentSize <= maxResidentSize) return;
            var idle = _residentSlots.Where(entry => entry.Value.Users == 0).OrderBy(entry => entry.Value.LastUse).ToList();
            foreach (var (key, slot) in idle)
            {
                if (_residentSize <= evictDownToSize) break;
                _residentSize -= sizeOf(slot.Value!.Value!);
                _residentSlots.Remove(key);
                _slots.Remove(key);
            }
        }
    }
}
