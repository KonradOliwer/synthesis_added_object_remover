namespace AddedObjectRemover.Tests.TimingAndDisposal;

public class DisposableUseTests
{
    private sealed class Resource : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void OnceReturnsTheUsesResultAndDisposesTheValueAfterIt()
    {
        var resource = new Resource();

        var disposedDuringUse = DisposableUse.Once(resource, used => used.Disposed);

        Assert.False(disposedDuringUse);
        Assert.True(resource.Disposed);
    }

    [Fact]
    public void OnceDisposesTheValueWhenTheUseThrows()
    {
        var resource = new Resource();

        Assert.Throws<InvalidOperationException>(() => DisposableUse.Once<Resource, int>(resource, _ => throw new InvalidOperationException()));

        Assert.True(resource.Disposed);
    }
}
