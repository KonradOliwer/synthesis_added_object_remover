using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacedObjectCollectorStopTests
{
    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: []);

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
        PlacedObjects.Collect(plugin, Mods, plan);

    private static PlacedRecordFact CreateTargetRecord() =>
        TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);
}
