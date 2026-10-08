using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>Unexpected errors for single objects are grouped by part and failure, so a faulty mesh gives one line, not thousands.</summary>
public class UnexpectedErrorLinesTests
{
    private const string Part = "checking objects for being too close";
    private const string Failure = "InvalidDataException: bad triangle data in meshes\\x.nif";
    private const string Consequence = "They were not removed.";
    private const int Failed = 3124;

    private static ReportContext Context(bool detailed) => new(Bases: null!, Shapes: null!, detailed);

    private static UnexpectedError ForObject(int position, string failure = Failure) =>
        new(Part, failure, Consequence, new ErrorSubject(SubjectKind.Object, $"Object{position:D4} [{position:X6}:Target.esp]", position));

    [Fact]
    public void ManyObjectsFailingTheSameWayAreOneLineWithTheCountAndAFewExamples()
    {
        var errors = Enumerable.Range(0, Failed).Select(position => ForObject(position));

        var lines = UnexpectedErrorLines.Of(errors, Context(detailed: false));

        Assert.Equal(
            $"  Unexpected error while {Part} ({Failure}): 3,124 objects, e.g. Object0000 [000000:Target.esp], "
            + "Object0001 [000001:Target.esp], Object0002 [000002:Target.esp] (+3,121 more). " + Consequence,
            Assert.Single(lines));
    }

    [Fact]
    public void TheDetailedLogAlsoListsEveryObject()
    {
        var errors = Enumerable.Range(0, Failed).Select(position => ForObject(position)).ToList();

        var lines = UnexpectedErrorLines.Of(errors, Context(detailed: true));

        Assert.Equal(1 + Failed, lines.Length);
        Assert.StartsWith("  Unexpected error while", lines[0], StringComparison.Ordinal);
        Assert.Equal(errors.Select(error => "    " + error.Subject!.Label), lines.Skip(1));
    }

    [Fact]
    public void TheLinesAreTheSameWhateverOrderTheErrorsCameIn()
    {
        var errors = Enumerable.Range(0, 20).Select(position => ForObject(position)).ToList();
        var shuffled = errors.OrderBy(error => error.Subject!.Position * 7 % 20).ToList();

        Assert.Equal(UnexpectedErrorLines.Of(errors, Context(detailed: true)).ToList(), UnexpectedErrorLines.Of(shuffled, Context(detailed: true)).ToList());
    }

    [Fact]
    public void DifferentFailuresAreSeparateGroupsOrderedByPartThenFailure()
    {
        var errors = new[] { ForObject(1, "Z failure"), ForObject(2, "A failure"), ForObject(3, "A failure") };

        var lines = UnexpectedErrorLines.Of(errors, Context(detailed: false));

        Assert.Equal(2, lines.Length);
        Assert.Contains("(A failure): 2 objects, e.g. Object0002", lines[0], StringComparison.Ordinal);
        Assert.Contains("(Z failure): 1 object, e.g. Object0001", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void AFewObjectsAreAllNamedWithoutAMoreCountOrAList()
    {
        var errors = Enumerable.Range(0, UnexpectedErrorLines.ExampleCount).Select(position => ForObject(position));

        var lines = UnexpectedErrorLines.Of(errors, Context(detailed: true));

        Assert.DoesNotContain("more", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorThatStoppedAWholePartKeepsItsOwnLine()
    {
        var error = new UnexpectedError("the also-remove step", "InvalidOperationException: broke", "Nothing was removed by it.");

        var lines = UnexpectedErrorLines.Of([error], Context(detailed: true));

        Assert.Equal("  Unexpected error in the also-remove step (InvalidOperationException: broke). Nothing was removed by it.", Assert.Single(lines));
    }

    [Fact]
    public void MeshesAreCountedAsMeshes()
    {
        var errors = new[] { @"meshes\b.nif", @"meshes\a.nif" }.Select(
            mesh => new UnexpectedError("reading mesh shapes", "InvalidDataException: broke", "Objects using the mesh have no shape.", new ErrorSubject(SubjectKind.Mesh, mesh, Position: 0)));

        var lines = UnexpectedErrorLines.Of(errors, Context(detailed: false));

        Assert.Equal(
            @"  Unexpected error while reading mesh shapes (InvalidDataException: broke): 2 meshes, e.g. meshes\a.nif, meshes\b.nif. Objects using the mesh have no shape.",
            Assert.Single(lines));
    }
}
