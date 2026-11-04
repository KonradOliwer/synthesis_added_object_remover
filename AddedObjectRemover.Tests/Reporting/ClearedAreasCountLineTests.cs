using System.Globalization;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The count line of the step for objects in cleared areas names every reason the step keeps an object, so the parts add up to the objects judged.</summary>
public sealed class ClearedAreasCountLineTests
{
    private const int MinObjectsAround = 5;

    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);

    [Fact]
    public void RemovedPlusKeptByEveryReasonEqualsTheObjectsJudged()
    {
        var evaluations = new[]
        {
            Check(0, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.RemovedInClearedArea),
            Check(1, ClearedAreaObjectKind.Invisible, LeftBehindOutcome.RemovedInClearedArea),
            Check(2, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.KeptProtectedType),
            Check(3, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.KeptTooFewObjectsAround),
            Check(4, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.KeptAreaStillStands),
            Check(5, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.KeptAreaStillStands),
            Check(6, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.RemovedLinkedToRemoval),
        };

        var found = ResultOf(evaluations);

        var line = FirstLine(found);

        Assert.Equal(
            "Objects in cleared areas: 3 removed (2 drawn, 1 invisible); "
            + "kept: 1 of a kind always kept, 1 with fewer than 5 of the cleaned mod's other objects around, 2 where the area still stands.",
            line);
        Assert.Equal(found.Evaluations.Count, RemovedAndKeptCounts(line).Sum());
    }

    /// <summary>The removed count and the count before each kept reason.</summary>
    private static IEnumerable<int> RemovedAndKeptCounts(string line)
    {
        var (removed, kept) = (line.Split(" removed")[0], line.Split("kept: ")[1]);
        return kept.Split(", ").Select(part => int.Parse(part.Split(' ')[0], CultureInfo.InvariantCulture)).Prepend(int.Parse(removed.Split(' ')[^1], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AKeptReasonWithNoObjectIsNotPrinted()
    {
        var line = FirstLine(ResultOf([Check(0, ClearedAreaObjectKind.Drawn, LeftBehindOutcome.KeptAreaStillStands)]));

        Assert.Equal("Objects in cleared areas: 0 removed; kept: 1 where the area still stands.", line);
    }

    private static ObjectsInClearedAreasResult ResultOf(IReadOnlyList<ClearedAreaCheck> evaluations) =>
        new(StepOutcome.Performed, evaluations, [], TimeSpan.Zero, TestRestingObjects.SwitchedOff, Failure: null);

    private static string FirstLine(ObjectsInClearedAreasResult found) =>
        LogSections.BuildObjectsInClearedAreasSection(TestScenes.CreateWorld([], []), found, [], MinObjectsAround, Normal).Lines[0];

    private static ClearedAreaCheck Check(int index, ClearedAreaObjectKind kind, LeftBehindOutcome decision) =>
        new(index, kind, ObjectsAround: 0, new AreaShare(0, 0, 0, 0, AreaVerdict.NoObjectsAround, 0, 0), decision, KeepReason: null);
}
