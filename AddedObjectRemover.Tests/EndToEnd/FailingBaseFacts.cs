namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The base facts of a run, except that reading one throws while <see cref="Armed"/>, so a test can break one part of the run only.</summary>
internal sealed class FailingBaseFacts(IBaseFacts inner) : IBaseFacts
{
    public const string FailureMessage = "base facts broke";

    public volatile bool Armed;

    public BaseFacts Of(BaseKey baseKey) => Armed ? throw new InvalidOperationException(FailureMessage) : inner.Of(baseKey);
}
