using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class RestingObjectsGatingTests
{
    private const int TargetCount = 3;
    private const float TouchGap = 1f;
    private const float SupportLost = 1f;

    private static AlsoRemoveSettings Options(FollowUpRemovalMode mode, bool touchChainsShown = false) =>
        new(mode, TouchGap, SupportLost, touchChainsShown);

    private static RemovalDecisions AfterTooClose(IReadOnlyList<int> seeds) =>
        TestSeededDecisions.Seed(ObjectsToKeep.Build(TestTargets.CreateMany(TargetCount), [], TestTargets.References(TargetCount)), TargetCount, seeds);

    private static RestingObjectsResult Result(FollowUpRemovalMode mode, bool hadSeeds) =>
        new(
            StepOutcome.Performed,
            mode,
            hadSeeds,
            [],
            [],
            [],
            ImmutableArray<RoundDetails>.Empty,
            new RestingObjectsWork(0, 0, 0, 0, PairTestStats.Zero),
            TimeSpan.Zero,
            TouchChains: null,
            Failure: null,
            TouchChainsFailure: null,
            ReportFileDetails.SwitchedOff);

    [Theory]
    [InlineData((int)FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, true)]
    [InlineData((int)FollowUpRemovalMode.OnlyWhatLosesItsSupport, false, false)]
    [InlineData((int)FollowUpRemovalMode.EverythingTouchingIt, true, false)]
    [InlineData((int)FollowUpRemovalMode.Nothing, true, false)]
    public void TheObjectsOfAnyPluginAreNeededOnlyForSupportRoundsWithSeeds(int mode, bool seeded, bool expected)
    {
        var decisions = AfterTooClose(seeded ? [1] : []);

        Assert.Equal(expected, RestingObjects.NeedsObjectsOfAnyPlugin(decisions, Options((FollowUpRemovalMode)mode)));
    }

    [Theory]
    [InlineData((int)FollowUpRemovalMode.EverythingTouchingIt, true, true, true)]
    [InlineData((int)FollowUpRemovalMode.EverythingTouchingIt, true, false, false)]
    [InlineData((int)FollowUpRemovalMode.EverythingTouchingIt, false, true, false)]
    [InlineData((int)FollowUpRemovalMode.OnlyWhatLosesItsSupport, true, true, false)]
    [InlineData((int)FollowUpRemovalMode.Nothing, true, true, false)]
    public void TheTouchChainsAreNeededOnlyForTouchRoundsWithSeedsThatAreShown(int mode, bool hadSeeds, bool shown, bool expected)
    {
        var result = Result((FollowUpRemovalMode)mode, hadSeeds);

        Assert.Equal(expected, RestingObjects.NeedsTouchChains(result, Options((FollowUpRemovalMode)mode, shown)));
    }
}
