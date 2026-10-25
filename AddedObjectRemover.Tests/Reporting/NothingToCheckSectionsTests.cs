using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>A run that finds nothing to check stops after collecting, and still logs its starting state.</summary>
public sealed class NothingToCheckSectionsTests
{
    private const int ObjectsOfCleanedMod = 3;

    [Fact]
    public void ARunWithNothingToCheckLogsItsStartingStateWithNothingJudgedOrRemoved()
    {
        var world = TestScenes.CreateWorld([], []);
        var stopped = world with
        {
            Counts = world.Counts with { CleanedModObjects = ObjectsOfCleanedMod, RemovedByEarlierPatchers = ObjectsOfCleanedMod },
            Stop = new StopReason(StopKind.NoTargetObjects, null),
        };

        var lines = TestReport.Over(new AssetProblemLog()).CollectedSectionLines(stopped);

        Assert.Equal(
            "Objects of Target.esp: 3; already removed by earlier patchers in this group: 3; "
            + "not judged (winning version from a later plugin, removed by a later plugin, deleted, start disabled, or without a valid position or rotation): 0; "
            + "judged in this run: 0; removed in this run: 0.",
            lines[^1]);
        Assert.DoesNotContain(lines, line => line.StartsWith("Using ", StringComparison.Ordinal));
    }
}
