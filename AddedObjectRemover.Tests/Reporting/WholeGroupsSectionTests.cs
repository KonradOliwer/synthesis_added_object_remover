using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The section of the step for whole groups counts and lists only the groups its round took objects from.</summary>
public sealed class WholeGroupsSectionTests
{
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void ASeededGroupThatLosesNothingIsNeitherCountedNorListed()
    {
        var (world, decisions) = Scene();
        var found = ResultOf([Check(1, 0, LeftBehindOutcome.RemovedInGroupWithClash), Check(2, 0, LeftBehindOutcome.RemovedLinkedToRemoval)]);

        var lines = LogSections.BuildWholeGroupsSection(world, found, decisions, [], TimeSpan.Zero, Detailed).Lines;

        Assert.StartsWith("Objects in groups with a clash removal: 2 removed in 1 group (largest 3)", lines[0], StringComparison.Ordinal);
        Assert.Single(lines, line => line.StartsWith("  Group of 3: seed removed ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("  Group of 1", StringComparison.Ordinal));
    }

    [Fact]
    public void SeededGroupsThatLoseNothingPrintNothing()
    {
        var (world, decisions) = Scene();

        Assert.Empty(LogSections.BuildWholeGroupsSection(world, ResultOf([]), decisions, [], TimeSpan.Zero, Detailed).Lines);
    }

    /// <summary>Four targets; 0 and 3 were removed as too close.</summary>
    private static (CollectedObjects World, IRemovalDecisions Decisions) Scene()
    {
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, 4).Select(index => TestTargets.Create(index, TestTargets.At(new Vector3(index * 100, 0, 0))))];
        var world = TestScenes.CreateWorld(targets, []) with
        {
            Spaces = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "TestSpace", SpaceKind.Worldspace, Name: null) },
        };
        var tooClose = new TooClose(new OtherId(0), new TooCloseCause.ObjectSize());
        var decisions = TestSeededDecisions.StartUnprotected(targets).Apply(RoundKind.TooClose, [new(new TargetId(0), tooClose), new(new TargetId(3), tooClose)]);
        return (world, decisions);
    }

    /// <summary>Group 0 holds targets 0 to 2 with seed 0; group 1 is target 3 alone, its own seed.</summary>
    private static WholeGroupsResult ResultOf(IReadOnlyList<WholeGroupCheck> evaluations) =>
        new(
            StepOutcome.Performed,
            [
                new SeededGroup([new TargetId(0), new TargetId(1), new TargetId(2)], new TargetId(0), TestTargets.Space, Vector2.Zero, new Vector2(200, 0)),
                new SeededGroup([new TargetId(3)], new TargetId(3), TestTargets.Space, new Vector2(300, 0), new Vector2(300, 0)),
            ],
            evaluations,
            [],
            TestRestingObjects.SwitchedOff,
            Failure: null);

    private static WholeGroupCheck Check(int target, int group, LeftBehindOutcome decision) => new(target, group, decision);
}
