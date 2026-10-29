namespace AddedObjectRemover.Tests.Rules;

/// <summary>What a step that judges groups reports across its passes.</summary>
public class JudgedPassesTests
{
    private static readonly PartKey Part = new("Test", "Part");

    [Fact]
    public void FailuresOfSeveralPassesAreListedInTargetOrderWithTheWholePartFailuresFirstInPassOrder()
    {
        var first = PassWith(WholePart("first pass"), Of("object 0", position: 0), Of("object 4", position: 4));
        var second = PassWith(WholePart("second pass"), Of("object 2", position: 2), Of("object 6", position: 6));

        var failures = new ChangedTerrainResult([first, second]).Failures;

        Assert.Equal(["first pass", "second pass", "object 0", "object 2", "object 4", "object 6"], failures.Select(failure => failure.Failure));
    }

    private static ChangedTerrainPass PassWith(params PartFailure[] failures) =>
        new([], StepOutcome.Performed, [], [], [.. failures], CellsCompared: 0, ObjectsWithoutTerrain: 0);

    private static PartFailure WholePart(string text) => new(Part, text, Subject: null);

    private static PartFailure Of(string text, int position) => new(Part, text, new FailureSubject($"key {position}", Name: null, position));
}
