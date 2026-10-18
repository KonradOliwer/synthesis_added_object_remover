using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// What earlier runs of a batch wrote is applied as the placed records are collected: a removed record is no object of any
/// kind and keeps nothing alive, a moved record stands at its new position, and the scan result stays as read.
/// </summary>
public class PlacedObjectCollectorEarlierRunsTests
{
    private const float Distance = 32f;
    private const float WithinDistance = 45f;
    private const float FarAway = 500f;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("EarlierRuns.esp");
    private static readonly PluginName OtherPlugin = new("Other.esp");
    private static readonly PluginName IgnoredPlugin = new("Ignored.esp");
    private static readonly PluginName EarlierTarget = new("EarlierTarget.esp");
    private static readonly PluginName Patch = new("Synthesis.esp");

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\earlier_runs_table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly IBaseObjectShapes Shapes = TestShapes.Create(Mod, "EarlierRunsData", Table);

    private static readonly WhatToCollect Plan = new(SupportOnlyObjects: true, Terrain: false, Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false);
    private static readonly IdentifiedMods Mods = new(TestTargets.TargetPlugin, new HashSet<PluginName> { IgnoredPlugin }, [], [], DetectedPatches.Off, []);

    private static readonly PlacedRecordFact Target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetPlugin, Vector3.Zero, Table.Base);

    [Fact]
    public void AnOtherModObjectAnEarlierRunRemovedIsLeftOutAndCounted()
    {
        var other = OtherRecord(1, new Vector3(WithinDistance, 0, 0));

        var before = Collect([Target, other], WrittenByEarlierRuns.None);
        var after = Collect([Target, other], Removed(other));

        Assert.Single(before.OtherModObjects);
        Assert.Empty(after.OtherModObjects);
        Assert.Equal(1, after.Counts.RemovedByEarlierRuns);
    }

    [Fact]
    public void AnOtherModObjectAnEarlierRunRemovedNoLongerMakesATargetTooClose()
    {
        var other = OtherRecord(1, new Vector3(WithinDistance, 0, 0));

        Assert.Single(FindTooClose(Collect([Target, other], WrittenByEarlierRuns.None)).Hits);
        Assert.Empty(FindTooClose(Collect([Target, other], Removed(other))).Hits);
    }

    [Fact]
    public void ASupportOnlyObjectAnEarlierRunRemovedIsLeftOut()
    {
        var ignored = TestPlacedRecords.Create(new RecordKey(IgnoredPlugin, 1), IgnoredPlugin, Vector3.Zero, Table.Base);

        Assert.Single(Collect([Target, ignored], WrittenByEarlierRuns.None).SupportOnlyObjects);
        Assert.Empty(Collect([Target, ignored], Removed(ignored)).SupportOnlyObjects);
    }

    /// <summary>
    /// The patch already holds the earlier run's removal, which reads back as Initially Disabled with an Enable Parent, below
    /// the world; that form counts as shown in game, so only the earlier runs' value makes it gone.
    /// </summary>
    [Fact]
    public void ARemovedObjectReadBackFromThePatchWithAnEnableParentIsLeftOut()
    {
        var readBack = TestPlacedRecords.Create(new RecordKey(EarlierTarget, 1), Patch, new Vector3(0, 0, -30000f), Table.Base) with
        {
            Placement = new PlacementFacts(InitiallyDisabled: true, HasEnableParent: true, new Vector3(0, 0, -30000f), Vector3.Zero),
        };

        Assert.Single(Collect([Target, readBack], WrittenByEarlierRuns.None).OtherModObjects);
        Assert.Empty(Collect([Target, readBack], Removed(readBack)).OtherModObjects);
    }

    [Fact]
    public void AMarkerAnEarlierRunMovedIsSeenAtItsNewPositionAndTheScanFactIsUnchanged()
    {
        var marker = TestPlacedRecords.Create(new RecordKey(EarlierTarget, 1), Patch, new Vector3(FarAway, 0, 0), Table.Base) with { HasMapMarker = true };
        var newPosition = new Vector3(WithinDistance, 0, 0);
        ImmutableArray<PlacedRecordFact> records = [Target, marker];

        var collected = Collect(records, Moved(marker, newPosition));

        Assert.Equal(newPosition, Assert.Single(collected.OtherModObjects).Position);
        Assert.Equal(new Vector3(FarAway, 0, 0), records[1].Placement.Position);
        Assert.Equal(1, collected.Counts.MovedByEarlierRuns);
    }

    [Fact]
    public void AnObjectAnEarlierRunMovedIsJudgedAtItsNewPosition()
    {
        var other = OtherRecord(1, new Vector3(FarAway, 0, 0));

        Assert.Empty(FindTooClose(Collect([Target, other], WrittenByEarlierRuns.None)).Hits);
        Assert.Single(FindTooClose(Collect([Target, other], Moved(other, new Vector3(WithinDistance, 0, 0)))).Hits);
    }

    [Fact]
    public void LinksFromAnObjectAnEarlierRunRemovedAreDropped()
    {
        var removed = OtherRecord(1, new Vector3(FarAway, 0, 0));
        var kept = OtherRecord(2, new Vector3(-FarAway, 0, 0));
        ImmutableArray<LinkFact> links = [LinkToTarget(removed), LinkToTarget(kept)];

        var collected = PlacedObjectCollector.Collect(new FixedFactsPlugin([Target, removed, kept], links), Mods, Plan, Removed(removed));

        Assert.Equal(kept.Key, Assert.Single(collected.Links).Source);
    }

    [Fact]
    public void OnlyRecordsInATargetSpaceAreCounted()
    {
        var outside = OtherRecord(1, Vector3.Zero) with { InTargetSpace = false };
        var movedOutside = OtherRecord(2, Vector3.Zero) with { InTargetSpace = false };
        var earlier = WrittenByEarlierRuns.None.With(new RecordWrites([outside.Key], [new RecordMove(movedOutside.Key, Vector3.One)]));

        var collected = Collect([Target, outside, movedOutside], earlier);

        Assert.Equal(0, collected.Counts.RemovedByEarlierRuns);
        Assert.Equal(0, collected.Counts.MovedByEarlierRuns);
    }

    private static PlacedRecordFact OtherRecord(uint id, Vector3 position) =>
        TestPlacedRecords.Create(new RecordKey(OtherPlugin, id), OtherPlugin, position, Table.Base);

    private static LinkFact LinkToTarget(PlacedRecordFact source) =>
        new(source.Key, SourceEditorId: null, "PlacedObject", Target.Key, RelationKind.LinkedReference, SourceIsPlaced: true, SourceIsWorldspace: false, AliasFillFlags.None);

    private static WrittenByEarlierRuns Removed(PlacedRecordFact record) =>
        WrittenByEarlierRuns.None.With(new RecordWrites([record.Key], []));

    private static WrittenByEarlierRuns Moved(PlacedRecordFact record, Vector3 to) =>
        WrittenByEarlierRuns.None.With(new RecordWrites([], [new RecordMove(record.Key, to)]));

    private static CollectedObjects Collect(ImmutableArray<PlacedRecordFact> records, WrittenByEarlierRuns earlier) =>
        PlacedObjectCollector.Collect(new FixedFactsPlugin(records), Mods, Plan, earlier);

    private static TooCloseResult FindTooClose(CollectedObjects collected)
    {
        var targets = collected.Targets;
        var world = TestScenes.Create(targets, collected.OtherModObjects, Shapes);
        var replacements = Replacements.None(collected.OtherModObjects.Length);
        return TooCloseObjects.Find(
            new TooCloseInput(
                targets,
                world.VisibleTargets,
                collected.Order,
                world.ObjectsThatCanCauseRemovals(replacements, NpcHandling.Never),
                world.NpcsThatCanSpawn(replacements),
                Shapes,
                new TriangleStore(Shapes.ReadTriangles),
                new Execution(1)),
            new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, Distance, ZoneShape.BoundingBox, NpcHandling.Never));
    }
}
