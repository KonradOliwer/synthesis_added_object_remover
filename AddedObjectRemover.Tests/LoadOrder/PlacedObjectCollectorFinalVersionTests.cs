using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// The collector decides by the creator first, then by the final version: a target record the target plugin or the patch wins
/// is judged where it is now, one another plugin wins is overridden later; the patch holds what earlier patchers in the group
/// wrote, so their moves and removals are read like any other version.
/// </summary>
public class PlacedObjectCollectorFinalVersionTests
{
    public enum Winner { Target, Patch, Later }

    private static readonly PluginName LaterPlugin = new("Later.esp");
    private static readonly PluginName EarlierTarget = new("EarlierTarget.esp");
    private static readonly Vector3 NewPlace = new(250, 0, 0);

    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [EarlierTarget, LaterPlugin]);

    [Fact]
    public void ATargetRecordThePatchMovedIsJudgedAtItsNewPlace()
    {
        var moved = TargetRecord(1, Winner.Patch, NewPlace);

        var collected = Collect([TargetRecord(0, Winner.Target, Vector3.Zero), moved]);

        Assert.Equal(NewPlace, Assert.Single(collected.Targets, target => target.Key == moved.Key).Transform.Position);
        Assert.Equal(0, collected.Counts.TargetsOverriddenLater);
    }

    [Fact]
    public void ATargetRecordALaterPluginWinsIsOverriddenLater()
    {
        var collected = Collect([TargetRecord(0, Winner.Target, Vector3.Zero), TargetRecord(1, Winner.Later, NewPlace)]);

        Assert.Single(collected.Targets);
        Assert.Equal(1, collected.Counts.TargetsOverriddenLater);
    }

    [Theory]
    [InlineData(Winner.Patch, 1, 0, 0)]
    [InlineData(Winner.Later, 0, 1, 0)]
    [InlineData(Winner.Target, 0, 0, 1)]
    public void ARemovedTargetRecordIsCountedByThePluginThatRemovesIt(Winner winner, int byEarlierPatchers, int byOtherPlugins, int hidden)
    {
        var collected = Collect([TargetRecord(0, Winner.Target, Vector3.Zero), Removed(TargetRecord(1, winner, NewPlace))]);

        Assert.Single(collected.Targets);
        Assert.Equal(
            new RemovedCounts(byEarlierPatchers, byOtherPlugins, hidden, OverriddenLater: 0),
            new RemovedCounts(
                collected.Counts.RemovedByEarlierPatchers,
                collected.Counts.RemovedByOtherPlugins,
                collected.Counts.TargetsHiddenOrWithoutPlacement,
                collected.Counts.TargetsOverriddenLater));
    }

    [Fact]
    public void EveryRecordTheTargetCreatedIsAnObjectOfTheCleanedModWhateverItsFinalVersion()
    {
        var otherRemovedByThePatch = Removed(TestPlacedRecords.Create(new RecordKey(EarlierTarget, 1), TestTargets.PatchName, Vector3.Zero));
        ImmutableArray<PlacedRecordFact> records =
        [
            TargetRecord(0, Winner.Target, Vector3.Zero),
            TargetRecord(1, Winner.Patch, NewPlace),
            TargetRecord(2, Winner.Later, NewPlace),
            Removed(TargetRecord(3, Winner.Patch, NewPlace)),
            Removed(TargetRecord(4, Winner.Later, NewPlace)),
            otherRemovedByThePatch,
        ];

        var counts = Collect(records).Counts;

        Assert.Equal(5, counts.CleanedModObjects);
        Assert.Equal(1, counts.RemovedByEarlierPatchers);
        Assert.Equal(1, counts.RemovedByOtherPlugins);
    }

    /// <summary>The patch's version of the record stands in a space that holds no record of the target, so the record cannot be judged there.</summary>
    [Fact]
    public void ATargetRecordThePatchMovedOutOfTheTargetSpacesCountsWithTheHiddenOnes()
    {
        var movedAway = TargetRecord(1, Winner.Patch, NewPlace) with { InTargetSpace = false };

        var collected = Collect([TargetRecord(0, Winner.Target, Vector3.Zero), movedAway]);

        Assert.DoesNotContain(collected.Targets, target => target.Key == movedAway.Key);
        Assert.Equal((2, 1), (collected.Counts.CleanedModObjects, collected.Counts.TargetsHiddenOrWithoutPlacement));
    }

    /// <summary>A kept marker an earlier run of another cleaned mod moved is read from the patch at its new place.</summary>
    [Fact]
    public void AnotherModsMarkerThePatchMovedIsAnOtherModObjectAtItsNewPlace()
    {
        var marker = TestPlacedRecords.Create(new RecordKey(EarlierTarget, 1), TestTargets.PatchName, NewPlace) with { HasMapMarker = true };

        var other = Assert.Single(Collect([TargetRecord(0, Winner.Target, Vector3.Zero), marker]).OtherModObjects);

        Assert.Equal(marker.Key, other.Key);
        Assert.Equal(NewPlace, other.Position);
    }

    private sealed record RemovedCounts(int ByEarlierPatchers, int ByOtherPlugins, int Hidden, int OverriddenLater);

    private static PlacedRecordFact TargetRecord(int index, Winner winner, Vector3 position) =>
        TestPlacedRecords.Create(TestTargets.Key(index), PluginOf(winner), position);

    private static PluginName PluginOf(Winner winner) => winner switch
    {
        Winner.Target => TestTargets.TargetName,
        Winner.Patch => TestTargets.PatchName,
        Winner.Later => LaterPlugin,
        _ => throw new ArgumentOutOfRangeException(nameof(winner), winner, null),
    };

    private static PlacedRecordFact Removed(PlacedRecordFact record) =>
        TestPlacedRecords.WithWinningPresence(record, new PresenceFacts(Deleted: false, InitiallyDisabled: true, EnableParent: null));

    private static CollectedObjects Collect(ImmutableArray<PlacedRecordFact> records) =>
        PlacedObjectCollector.Collect(new FixedFactsPlugin(records), Mods, Plan);
}
