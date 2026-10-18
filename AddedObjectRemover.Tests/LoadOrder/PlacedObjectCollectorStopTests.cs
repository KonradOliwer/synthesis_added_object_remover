using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacedObjectCollectorStopTests
{
    private static readonly WhatToCollect Plan = new(SupportOnlyObjects: false, Terrain: false, Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false);
    private static readonly IdentifiedMods Mods = new(TestTargets.TargetPlugin, new HashSet<PluginName>(), [], [], DetectedPatches.Off, []);

    [Fact]
    public void NoTargetObjectsStopsTheRun()
    {
        var world = Collect(new FixedFactsPlugin([]), Plan);

        Assert.Equal(new StopReason(StopKind.NoTargetObjects, null), world.Stop);
    }

    [Fact]
    public void TargetObjectsLetTheRunGoOn()
    {
        var world = Collect(new FixedFactsPlugin([CreateTargetRecord()]), Plan);

        Assert.Single(world.Targets);
        Assert.Null(world.Stop);
    }

    [Fact]
    public void TheSupportOnlyFlagFollowsThePlan()
    {
        var records = new FixedFactsPlugin([CreateTargetRecord()]);

        var without = Collect(records, Plan);
        var with = Collect(records, Plan with { SupportOnlyObjects = true });

        Assert.False(without.SupportOnlyObjectsCollected);
        Assert.True(with.SupportOnlyObjectsCollected);
        Assert.Empty(with.RequireSupportOnlyObjects());
    }

    [Fact]
    public void ReadingSupportOnlyObjectsThatWereNotCollectedThrows()
    {
        var world = Collect(new FixedFactsPlugin([CreateTargetRecord()]), Plan);

        Assert.Empty(world.SupportOnlyObjects);
        Assert.Throws<InvalidOperationException>(() => world.RequireSupportOnlyObjects());
    }

    [Fact]
    public void ThePersistentFlagTheScriptNamedRecordAndTheOwnLocationRefTypeAreCopiedToTheTargetObject()
    {
        var named = new ScriptNamedRecord(TestTargets.Key(1), "SomeQuest", ScriptNamedKind.Quest);
        var record = CreateTargetRecord() with { IsPersistent = true, NamedByScript = named, HasLocationRefType = true };

        var target = Assert.Single(Collect(new FixedFactsPlugin([record]), Plan).Targets);

        Assert.True(target.IsPersistent);
        Assert.Equal(named, target.NamedByScript);
        Assert.True(target.HasLocationRefType);
    }

    private static CollectedObjects Collect(IPluginRecords plugin, WhatToCollect plan) =>
        PlacedObjectCollector.Collect(plugin, Mods, plan, WrittenByEarlierRuns.None);

    private static PlacedRecordFact CreateTargetRecord() =>
        TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetPlugin, Vector3.Zero);
}
