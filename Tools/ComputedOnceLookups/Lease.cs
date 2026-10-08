namespace AddedObjectRemover;

/// <summary>Ends the use of one slot of a store; implemented by the store that hands out leases.</summary>
public interface ILeaseOwner<T> where T : class
{
    void EndUse(StoreSlot<T> slot);
}

/// <summary>One use of a stored value; disposing it ends the use. A default lease holds nothing.</summary>
public readonly struct Lease<T> : IDisposable where T : class
{
    private readonly ILeaseOwner<T>? _owner;
    private readonly StoreSlot<T>? _slot;

    internal Lease(T? value, ILeaseOwner<T> owner, StoreSlot<T> slot)
    {
        Value = value;
        _owner = owner;
        _slot = slot;
    }

    /// <summary>Null when the store's build function gave nothing for the key.</summary>
    public T? Value { get; }

    public void Dispose()
    {
        if (_owner != null && _slot != null) _owner.EndUse(_slot);
    }
}
