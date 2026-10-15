namespace AddedObjectRemover.Tests.Entry;

public class UnexpectedErrorsTests
{
    private static readonly PartKey WholePart = new("Owner", "Whole");
    private static readonly PartKey PerThing = new("Owner", "PerThing");

    private static PartFailure Whole(string failure) => new(WholePart, failure, Subject: null);

    private static PartFailure ForThing(string failure, int position) =>
        new(PerThing, failure, new FailureSubject($"{0x800 + position:X6}:Other.esp", "Editor" + position, position));

    [Fact]
    public void AFailureRecordedManyTimesAppearsOnceAfterAMark()
    {
        var errors = new UnexpectedErrors();
        var mark = errors.Mark();

        errors.Add(Whole("boom"));
        errors.Add(Whole("boom"));
        errors.Add(ForThing("boom", 3));
        errors.Add(ForThing("boom", 3));

        Assert.Equal([ForThing("boom", 3), Whole("boom")], errors.Since(mark).OrderBy(failure => failure.Part.Name, StringComparer.Ordinal));
        Assert.Empty(errors.Since(errors.Mark()));
    }

    [Fact]
    public void TheFirstRecordOfAFailureWinsSoARepeatAfterAMarkIsNotNew()
    {
        var errors = new UnexpectedErrors();
        errors.Add(Whole("boom"));
        var mark = errors.Mark();

        errors.Add(Whole("boom"));

        Assert.Empty(errors.Since(mark));
        Assert.Equal(mark, errors.Mark());
    }

    [Fact]
    public void AMarkSeparatesTheFailuresRecordedBeforeItFromThoseRecordedAfter()
    {
        var errors = new UnexpectedErrors();
        errors.Add(Whole("early"));
        errors.Add(ForThing("early", 1));
        var mark = errors.Mark();
        errors.Add(ForThing("late", 2));
        errors.Add(Whole("late"));

        Assert.Equal(["late", "late"], errors.Since(mark).Select(failure => failure.Failure));
        Assert.Equal(4, errors.Since(default).Length);
        Assert.Empty(errors.Since(errors.Mark()));
    }
}
