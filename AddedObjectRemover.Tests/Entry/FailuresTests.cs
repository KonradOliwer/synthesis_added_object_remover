namespace AddedObjectRemover.Tests.Entry;

public class FailuresTests
{
    [Fact]
    public void AnAttemptedPartThatThrowsGivesTheStoppedResultBuiltFromItsFailure()
    {
        var result = Failures.Attempt<string>(() => throw new InvalidOperationException("it broke."), failure => $"stopped: {failure}");

        Assert.Equal("stopped: InvalidOperationException: it broke", result);
        Assert.Equal("worked", Failures.Attempt(() => "worked", failure => failure));
        Assert.Throws<OutOfMemoryException>(() => Failures.Attempt<string>(() => throw new OutOfMemoryException(), failure => failure));
    }

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
