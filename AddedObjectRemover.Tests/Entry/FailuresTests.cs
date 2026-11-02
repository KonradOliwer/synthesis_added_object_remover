using System.Diagnostics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Entry;

public class FailuresTests
{
    [Fact]
    public void AnAttemptedPartThatFindsTheUsersDataUnreadableGivesTheStoppedResultBuiltFromItsFailure()
    {
        var result = Failures.Attempt<string>(() => throw new IOException("it broke."), failure => $"stopped: {failure.Description}");

        Assert.Equal("stopped: IOException: it broke", result);
        Assert.Equal("worked", Failures.Attempt(() => "worked", failure => failure.Description));
    }

    [Fact]
    public void EachKnownUserDataFailureIsRecoverable()
    {
        Assert.True(Failures.IsRecoverable(new IOException()));
        Assert.True(Failures.IsRecoverable(new FileNotFoundException()));
        Assert.True(Failures.IsRecoverable(new UnauthorizedAccessException()));
        Assert.True(Failures.IsRecoverable(new PlantedUserDataException()));
    }

    [Fact]
    public void ABugEndsTheRunInsteadOfBeingRecorded()
    {
        Exception[] bugs =
        [
            new UnreachableException("A bug of ours."),
            new InvalidOperationException("A bug of ours."),
            new NullReferenceException(),
            new ArgumentException("A bug of ours."),
            new IndexOutOfRangeException(),
            new OutOfMemoryException(),
        ];

        foreach (var bug in bugs)
        {
            Assert.False(Failures.IsRecoverable(bug));
            Assert.Same(bug, Assert.Throws(bug.GetType(), () => Failures.Attempt<string>(() => throw bug, failure => failure.Description)));
        }
    }

    [Fact]
    public void ACaughtFailureKeepsTheExceptionsFullTextWithItsStackTrace()
    {
        var details = Failures.Attempt<string>(ThrowFromNamedMethod, failure => failure.Details);

        Assert.StartsWith("System.IO.IOException: it broke.", details, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowFromNamedMethod), details, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailsOfAWrappedParallelFailureAreThoseOfTheInnerFailureWhoseDescriptionSortsFirst()
    {
        var wrapped = new AggregateException(new InvalidOperationException("b"), new ArgumentException("a"));

        Assert.StartsWith("System.ArgumentException: a", Failures.Catch(wrapped).Details, StringComparison.Ordinal);
    }

    private static string ThrowFromNamedMethod() => throw new IOException("it broke.");

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
    public void AFailureWrappedByParallelWorkIsRecoverableOnlyWhenEveryInnerFailureIs()
    {
        var onlyUserData = new AggregateException(new IOException("a"), new AggregateException(new PlantedUserDataException()));
        var withABug = new AggregateException(new IOException("a"), new AggregateException(new InvalidOperationException("b")));
        var withOutOfMemory = new AggregateException(new IOException("a"), new AggregateException(new OutOfMemoryException()));

        Assert.True(Failures.IsRecoverable(onlyUserData));
        Assert.False(Failures.IsRecoverable(withABug));
        Assert.False(Failures.IsRecoverable(withOutOfMemory));
        Assert.False(Failures.IsRecoverable(new AggregateException()));
        Assert.False(Failures.IsRecoverable(new AggregateException(new AggregateException())));
        Assert.Throws<AggregateException>(() => Failures.Attempt<int>(() => throw withABug, _ => -1));
    }
}
