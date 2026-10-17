using System.Numerics;

namespace AddedObjectRemover.Tests.Rules;

public sealed class WrittenByEarlierRunsTests
{
    private static readonly RecordKey First = new(new PluginName("A.esp"), 1);
    private static readonly RecordKey Second = new(new PluginName("A.esp"), 2);
    private static readonly RecordKey Third = new(new PluginName("B.esp"), 3);

    [Fact]
    public void NoneIsEmpty()
    {
        Assert.Empty(WrittenByEarlierRuns.None.Disabled);
        Assert.Empty(WrittenByEarlierRuns.None.MovedTo);
    }

    [Fact]
    public void WithAddsTheWritesOfEachRunAndLeavesTheEarlierValueUnchanged()
    {
        var afterFirst = WrittenByEarlierRuns.None.With(new RecordWrites([First, Second], [new RecordMove(Third, Vector3.One)]));
        var afterSecond = afterFirst.With(new RecordWrites([Third], []));

        Assert.Equal([First, Second], afterFirst.Disabled.OrderBy(key => key.Id));
        Assert.Equal([First, Second, Third], afterSecond.Disabled.OrderBy(key => key.Id));
        Assert.Equal(Vector3.One, afterSecond.MovedTo[Third]);
        Assert.Empty(WrittenByEarlierRuns.None.Disabled);
    }

    [Fact]
    public void ALaterMoveOfTheSameRecordWins()
    {
        var written = WrittenByEarlierRuns.None
            .With(new RecordWrites([], [new RecordMove(First, Vector3.One)]))
            .With(new RecordWrites([], [new RecordMove(First, Vector3.UnitX)]));

        Assert.Equal(Vector3.UnitX, Assert.Single(written.MovedTo).Value);
    }
}
