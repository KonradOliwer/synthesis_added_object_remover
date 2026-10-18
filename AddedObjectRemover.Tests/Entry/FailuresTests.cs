namespace AddedObjectRemover.Tests.Entry;

public class FailuresTests
{
    [Fact]
    public void AnAttemptedPartThatThrowsGivesTheStoppedResultBuiltFromItsFailure()
    {
        var result = Failures.Attempt<string>(() => throw new InvalidOperationException("it broke."), failure => $"stopped: {failure.Description}");

        Assert.Equal("stopped: InvalidOperationException: it broke", result);
        Assert.Equal("worked", Failures.Attempt(() => "worked", failure => failure.Description));
        Assert.Throws<OutOfMemoryException>(() => Failures.Attempt<string>(() => throw new OutOfMemoryException(), failure => failure.Description));
    }

    [Fact]
    public void ACaughtFailureKeepsTheExceptionsFullTextWithItsStackTrace()
    {
        var details = Failures.Attempt<string>(ThrowFromNamedMethod, failure => failure.Details);

        Assert.StartsWith("System.InvalidOperationException: it broke.", details, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowFromNamedMethod), details, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailsOfAWrappedParallelFailureAreThoseOfTheInnerFailureWhoseDescriptionSortsFirst()
    {
        var wrapped = new AggregateException(new InvalidOperationException("b"), new ArgumentException("a"));

        Assert.StartsWith("System.ArgumentException: a", Failures.Catch(wrapped).Details, StringComparison.Ordinal);
    }

    private static string ThrowFromNamedMethod() => throw new InvalidOperationException("it broke.");

    [Fact]
    public void AMultiLineMessageIsReportedOnOneLine()
    {
        Assert.Equal("ArgumentException: first line second line", Failures.DescribeFailure(new ArgumentException("first line\r\nsecond line")));
    }

    [Fact]
    public void AWrappedParallelFailureListsItsDistinctInnerFailuresInAFixedOrder()
    {
        var wrapped = new AggregateException(
            new InvalidOperationException("b"), new ArgumentException("a"), new InvalidOperationException("b"));

        Assert.Equal("ArgumentException: a; InvalidOperationException: b", Failures.DescribeFailure(wrapped));
    }

    [Fact]
    public void RunningOutOfMemoryInsideParallelWorkIsNotCarriedOnFrom()
    {
        var wrapped = new AggregateException(
            new InvalidOperationException("a"), new AggregateException(new OutOfMemoryException()));

        Assert.False(Failures.IsRecoverable(wrapped));
        Assert.Throws<AggregateException>(() => Failures.Attempt<int>(() => throw wrapped, _ => -1));
        Assert.True(Failures.IsRecoverable(new AggregateException(new InvalidOperationException("a"))));
    }

    [Fact]
    public void RunningOutOfMemoryIsNotCarriedOnFrom()
    {
        Assert.Throws<OutOfMemoryException>(() => Failures.Attempt<int>(() => throw new OutOfMemoryException(), _ => -1));
    }
}
