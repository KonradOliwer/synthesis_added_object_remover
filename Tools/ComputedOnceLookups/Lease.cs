namespace AddedObjectRemover;

/// <summary>Ends the use of one slot of a store; implemented by the store that hands out leases.</summary>
public interface ILeaseOwner<T> where T : class
{
    void EndUse(StoreSlot<T> slot);
}

/// <summary>
/// One use of a stored value; disposing it ends the use. A class, not a struct, so every holder shares one lease and the
/// use ends once however often it is disposed.
/// </summary>
public sealed class Lease<T> : IDisposable where T : class
{
    private readonly StoreSlot<T> _slot;
    private ILeaseOwner<T>? _owner;

    internal Lease(T? value, ILeaseOwner<T> owner, StoreSlot<T> slot)
    {
        Value = value;
        _owner = owner;
        _slot = slot;
    }

    /// <summary>Null when the store's build function gave nothing for the key.</summary>
    public T? Value { get; }

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndUse(_slot);
}
