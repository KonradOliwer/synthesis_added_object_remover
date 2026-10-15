using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacedObjectCollectorStopTests
{
    private static readonly WhatToCollect Plan = new(SupportOnlyObjects: false, Terrain: false, Navmesh: false, OtherEditorIds: false, OverriddenOthersList: false, TerrainVersions: false, TerrainLayers: false);
    private static readonly IdentifiedMods Mods = new(TestTargets.TargetPlugin, new HashSet<PluginName>(), [], [], DetectedPatches.Off, [], []);

    [Fact]
    public void NoTargetObjectsStopsTheRun()
    {
        var world = PlacedObjectCollector.Collect(new FixedFactsPlugin([]), Mods, Plan);

        Assert.Equal(new StopReason(StopKind.NoTargetObjects, null), world.Stop);
    }

    [Fact]
    public void TargetObjectsLetTheRunGoOn()
    {
        var world = PlacedObjectCollector.Collect(new FixedFactsPlugin([CreateTargetRecord()]), Mods, Plan);

        Assert.Single(world.Targets);
        Assert.Null(world.Stop);
    }

    [Fact]
    public void TheSupportOnlyFlagFollowsThePlan()
    {
        var records = new FixedFactsPlugin([CreateTargetRecord()]);

        var without = PlacedObjectCollector.Collect(records, Mods, Plan);
        var with = PlacedObjectCollector.Collect(records, Mods, Plan with { SupportOnlyObjects = true });

        Assert.False(without.SupportOnlyObjectsCollected);
        Assert.True(with.SupportOnlyObjectsCollected);
        Assert.Empty(with.RequireSupportOnlyObjects());
    }

    [Fact]
    public void ReadingSupportOnlyObjectsThatWereNotCollectedThrows()
    {
        var world = PlacedObjectCollector.Collect(new FixedFactsPlugin([CreateTargetRecord()]), Mods, Plan);

        Assert.Empty(world.SupportOnlyObjects);
        Assert.Throws<InvalidOperationException>(() => world.RequireSupportOnlyObjects());
    }

    private static PlacedRecordFact CreateTargetRecord() =>
        new(
            TestTargets.Key(0),
            EditorId: null,
            TestTargets.TargetPlugin,
            new SpaceFact(TestTargets.Space, null, SpaceKind.Worldspace),
            new CellFact(TestTargets.Space, null),
            InTargetSpace: true,
            new PlacementFacts(InitiallyDisabled: false, HasEnableParent: false, Vector3.Zero, Vector3.Zero),
            Scale: null,
            Base: null,
            IsTeleportDoor: false,
            IsPrimitive: false,
            HasMapMarker: false,
            ReferenceRadius: null,
            PrimitiveBounds: null);

    private sealed class FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records) : IPluginRecords
    {
        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) =>
            new(records, records.Length, new HashSet<RecordKey>(), [], NavmeshCount: 0);

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => [];

        public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

        public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();
        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
