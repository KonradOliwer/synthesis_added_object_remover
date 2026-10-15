namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The base facts of a run, except that reading one throws while <see cref="Armed"/>, or once after
/// <see cref="FailNextRead"/>, so a test can break one part of the run only.
/// </summary>
internal sealed class FailingBaseFacts(IBaseFacts inner) : IBaseFacts
{
    public const string FailureMessage = "base facts broke";

    private int _failNextRead;

    public volatile bool Armed;

    public void FailNextRead() => Interlocked.Exchange(ref _failNextRead, 1);

    public BaseFacts Of(BaseKey baseKey) =>
        Armed || Interlocked.Exchange(ref _failNextRead, 0) == 1 ? throw new InvalidOperationException(FailureMessage) : inner.Of(baseKey);
}
