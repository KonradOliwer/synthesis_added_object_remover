namespace AddedObjectRemover;

/// <summary>One use of a disposable value, as an expression.</summary>
public static class DisposableUse
{
    /// <summary>The result of <paramref name="use"/> on the value, which is disposed after it, also when the use throws.</summary>
    public static TResult Once<TDisposable, TResult>(TDisposable value, Func<TDisposable, TResult> use)
        where TDisposable : IDisposable
    {
        using (value) return use(value);
    }
}
