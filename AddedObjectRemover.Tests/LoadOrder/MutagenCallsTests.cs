using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// What the library's getters throw for malformed plugin bytes counts as malformed data by its type alone, the context wrappers
/// counting as what they wrap; everything else, and every index of ours outside the getters, stays a bug.
/// </summary>
public class MutagenCallsTests
{
    private const string Message = "malformed";

    public static TheoryData<Exception> MalformedData => new()
    {
        new MalformedDataException(Message),
        new ModGroupsMalformedException(),
        new ArgumentOutOfRangeException(Message),
        new IndexOutOfRangeException(Message),
        new NotImplementedException(Message),
        RecordWrapping(inner: null),
        RecordWrapping(new MalformedDataException(Message)),
        SubrecordWrapping(new IndexOutOfRangeException(Message)),
        RecordWrapping(SubrecordWrapping(new ArgumentOutOfRangeException(Message))),
        new AggregateException(new MalformedDataException(Message), new AggregateException(new NotImplementedException(Message))),
    };

    public static TheoryData<Exception> Bugs => new()
    {
        new NullReferenceException(Message),
        new InvalidOperationException(Message),
        new ArgumentException(Message),
        new KeyNotFoundException(Message),
        RecordWrapping(new NullReferenceException(Message)),
        SubrecordWrapping(new InvalidCastException(Message)),
        new AggregateException(new MalformedDataException(Message), new NullReferenceException(Message)),
        new AggregateException(),
        new AggregateException(new AggregateException()),
    };

    [Theory]
    [MemberData(nameof(MalformedData))]
    public void WhatTheGettersThrowForMalformedBytesBecomesMalformedUserData(Exception thrown)
    {
        var failure = Assert.Throws<MalformedRecordException>(() => MutagenCalls.Read<int>(() => throw thrown));

        Assert.Same(thrown, failure.InnerException);
        Assert.True(Failures.IsRecoverable(failure));
    }

    [Theory]
    [MemberData(nameof(Bugs))]
    public void AnythingElseThrownInsideTheGettersStaysABug(Exception thrown)
    {
        var failure = Record.Exception(() => MutagenCalls.Read<int>(() => throw thrown));

        Assert.Same(thrown, failure);
        Assert.False(Failures.IsRecoverable(failure));
    }

    [Fact]
    public void OurOwnBadIndexIntoAMapTheGettersReturnedStaysABug()
    {
        var land = new Landscape(FormKey.Factory("000800:Base.esm"), SkyrimRelease.SkyrimSE)
        {
            VertexHeightMap = new LandscapeVertexHeightMap { HeightMap = new Array2d<sbyte>(new P2Int(TerrainGrid.VerticesPerSide, TerrainGrid.VerticesPerSide), default) },
        };
        var map = MutagenCalls.Read(() => land.VertexHeightMap!.HeightMap);

        var failure = Record.Exception(() => map[TerrainGrid.VerticesPerSide, 0]);

        Assert.NotNull(failure);
        Assert.IsNotType<MalformedRecordException>(failure);
        Assert.False(Failures.IsRecoverable(failure));
    }

    private static RecordException RecordWrapping(Exception? inner) =>
        new(formKey: null, recordType: null, modKey: null, edid: null, message: Message, innerException: inner);

    private static SubrecordException SubrecordWrapping(Exception inner) =>
        new(new RecordType("EDID"), formKey: null, majorRecordType: null, modKey: null, edid: null, message: Message, innerException: inner);
}
