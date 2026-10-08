namespace AddedObjectRemover.Tests.Entry;

public class FailuresTests
{
    [Fact]
    public void AGuardedPartThatWorksGivesItsOwnResultAndReportsNothing()
    {
        var reported = new List<UnexpectedError>();

        var result = Failures.Guard(reported.Add, "the part", "It is missing.", () => 7, notRun: () => -1);

        Assert.Equal(7, result);
        Assert.Empty(reported);
    }

    [Fact]
    public void AGuardedPartThatThrowsGivesTheNotRunResultAndReportsItsScope()
    {
        var reported = new List<UnexpectedError>();

        var result = Failures.Guard<int>(
            reported.Add, "the part", "The part did not run.", () => throw new InvalidOperationException("it broke."), notRun: () => -1);

        Assert.Equal(-1, result);
        Assert.Equal(
            "  Unexpected error in the part (InvalidOperationException: it broke). The part did not run.",
            Assert.Single(reported).Message);
    }

    [Fact]
    public void AMultiLineMessageIsReportedOnOneLine()
    {
        var error = Failures.Unexpected("the part", "Gone.", new ArgumentException("first line\r\nsecond line"));

        Assert.Equal("  Unexpected error in the part (ArgumentException: first line second line). Gone.", error.Message);
    }

    [Fact]
    public void AWrappedParallelFailureListsItsDistinctInnerFailuresInAFixedOrder()
    {
        var wrapped = new AggregateException(
            new InvalidOperationException("b"), new ArgumentException("a"), new InvalidOperationException("b"));

        Assert.Equal("ArgumentException: a; InvalidOperationException: b", Failures.Describe(wrapped));
    }

    [Fact]
    public void RunningOutOfMemoryInsideParallelWorkIsNotCarriedOnFrom()
    {
        var reported = new List<UnexpectedError>();
        var wrapped = new AggregateException(
            new InvalidOperationException("a"), new AggregateException(new OutOfMemoryException()));

        Assert.False(Failures.IsRecoverable(wrapped));
        Assert.Throws<AggregateException>(
            () => Failures.Guard<int>(reported.Add, "the part", "Gone.", () => throw wrapped, notRun: () => -1));
        Assert.Empty(reported);
        Assert.True(Failures.IsRecoverable(new AggregateException(new InvalidOperationException("a"))));
    }

    [Fact]
    public void RunningOutOfMemoryIsNotCarriedOnFrom()
    {
        var reported = new List<UnexpectedError>();

        Assert.Throws<OutOfMemoryException>(
            () => Failures.Guard<int>(reported.Add, "the part", "Gone.", () => throw new OutOfMemoryException(), notRun: () => -1));
        Assert.Empty(reported);
    }
}
