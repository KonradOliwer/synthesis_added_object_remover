using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// A record whose final version is removed in the game is no object of any kind and its links keep nothing, in each of the
/// three removed forms; a record only standing at the removed depth is still an object.
/// </summary>
public class PlacedObjectCollectorRemovedTests
{
    public enum RemovedKind { Deleted, InitiallyDisabledWithoutEnableParent, PlayerAsOppositeEnableParent }

    private const float FarAway = 500f;

    private static readonly PluginName OtherPlugin = new("Other.esp");
    private static readonly PluginName IgnoredPlugin = new("Ignored.esp");

    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [IgnoredPlugin], untrusted: [OtherPlugin]);

    private static readonly PlacedRecordFact Target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);

    [Theory]
    [InlineData(RemovedKind.Deleted)]
    [InlineData(RemovedKind.InitiallyDisabledWithoutEnableParent)]
    [InlineData(RemovedKind.PlayerAsOppositeEnableParent)]
    public void ARemovedOtherModRecordIsNoOtherModObject(RemovedKind kind)
    {
        var other = OtherRecord(1, new Vector3(FarAway, 0, 0));

        Assert.Single(Collect([Target, other]).OtherModObjects);
        Assert.Empty(Collect([Target, Removed(other, kind)]).OtherModObjects);
    }

    [Theory]
    [InlineData(RemovedKind.Deleted)]
    [InlineData(RemovedKind.InitiallyDisabledWithoutEnableParent)]
    [InlineData(RemovedKind.PlayerAsOppositeEnableParent)]
    public void ARemovedRecordOfAnIgnoredPluginIsNoSupportOnlyObject(RemovedKind kind)
    {
        var ignored = TestPlacedRecords.Create(new RecordKey(IgnoredPlugin, 1), IgnoredPlugin, new Vector3(FarAway, 0, 0));

        Assert.Single(Collect([Target, ignored]).SupportOnlyObjects);
        Assert.Empty(Collect([Target, Removed(ignored, kind)]).SupportOnlyObjects);
    }

    [Theory]
    [InlineData(RemovedKind.Deleted)]
    [InlineData(RemovedKind.InitiallyDisabledWithoutEnableParent)]
    [InlineData(RemovedKind.PlayerAsOppositeEnableParent)]
    public void ARemovedWinningTargetRecordIsNoTargetAndCountsAsHidden(RemovedKind kind)
    {
        var removed = Removed(TestPlacedRecords.Create(TestTargets.Key(1), TestTargets.TargetName, new Vector3(FarAway, 0, 0)), kind);

        var collected = Collect([Target, removed]);

        Assert.Equal(Target.Key, Assert.Single(collected.Targets).Key);
        Assert.Equal(1, collected.Counts.TargetsHiddenOrWithoutPlacement);
    }

    [Theory]
    [InlineData(RemovedKind.Deleted)]
    [InlineData(RemovedKind.InitiallyDisabledWithoutEnableParent)]
    [InlineData(RemovedKind.PlayerAsOppositeEnableParent)]
    public void LinksFromARemovedRecordAreDropped(RemovedKind kind)
    {
        var removed = Removed(OtherRecord(1, new Vector3(FarAway, 0, 0)), kind);
        var kept = OtherRecord(2, new Vector3(-FarAway, 0, 0));

        var collected = Collect([Target, removed, kept], [LinkToTarget(removed), LinkToTarget(kept)]);

        Assert.Equal(kept.Key, Assert.Single(collected.Links).Source);
    }

    [Fact]
    public void LinksFromARemovedRecordOutsideTheTargetSpacesAreDropped()
    {
        var removed = Removed(OtherRecord(1, Vector3.Zero), RemovedKind.InitiallyDisabledWithoutEnableParent) with { InTargetSpace = false };
        var kept = OtherRecord(2, Vector3.Zero) with { InTargetSpace = false };

        var collected = Collect([Target, removed, kept], [LinkToTarget(removed), LinkToTarget(kept)]);

        Assert.Equal(kept.Key, Assert.Single(collected.Links).Source);
    }

    [Fact]
    public void ARecordAtTheRemovedDepthWithoutARemovedFormIsStillAnObject()
    {
        var sunk = OtherRecord(1, new Vector3(FarAway, 0, RemovalConvention.RemovedZ));

        var other = Assert.Single(Collect([Target, sunk]).OtherModObjects);

        Assert.Equal(RemovalConvention.RemovedZ, other.Position.Z);
    }

    private static PlacedRecordFact OtherRecord(uint id, Vector3 position) =>
        TestPlacedRecords.Create(new RecordKey(OtherPlugin, id), OtherPlugin, position);

    private static PlacedRecordFact Removed(PlacedRecordFact record, RemovedKind kind) =>
        TestPlacedRecords.WithWinningPresence(record, PresenceOf(kind));

    private static PresenceFacts PresenceOf(RemovedKind kind) => kind switch
    {
        RemovedKind.Deleted => new PresenceFacts(Deleted: true, InitiallyDisabled: false, EnableParent: null),
        RemovedKind.InitiallyDisabledWithoutEnableParent => new PresenceFacts(Deleted: false, InitiallyDisabled: true, EnableParent: null),
        RemovedKind.PlayerAsOppositeEnableParent => new PresenceFacts(
            Deleted: false, InitiallyDisabled: false, new EnableParentFact(RemovalConvention.PlayerRef, Opposite: true)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static LinkFact LinkToTarget(PlacedRecordFact source) =>
        new(source.Key, SourceEditorId: null, "PlacedObject", Target.Key, RelationKind.LinkedReference, SourceIsPlaced: true, SourceIsWorldspace: false, AliasFillFlags.None);

    private static CollectedObjects Collect(ImmutableArray<PlacedRecordFact> records) => Collect(records, []);

    private static CollectedObjects Collect(ImmutableArray<PlacedRecordFact> records, ImmutableArray<LinkFact> links) =>
        PlacedObjectCollector.Collect(new FixedFactsPlugin(records, links), Mods, Plan);
}
