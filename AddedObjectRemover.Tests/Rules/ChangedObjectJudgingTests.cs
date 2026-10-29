using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// What a removal writes for each target object, and the check of an earlier version at its old place in the world without the
/// cleaned mod. The barrel is put at A by the base game, at B by CityMod.esp (the earlier plugin) and at C by the cleaned mod. Other
/// plugins put a crate next to B: FenceMod.esp (untrusted), GrassPatcher.esp (never trusted), both loading without the cleaned mod,
/// and CompatPatch.esp, which needs the cleaned mod and is trusted for it.
/// </summary>
public class ChangedObjectJudgingTests
{
    private const float MovedDistance = 10f;
    private const float RemovalDistance = 50f;
    private const int Ground = 0;

    /// <summary>The terrain vertex under the barrel's old place (1024, 1024 is the eighth vertex of the eighth row), and its height when something raised it.</summary>
    private const int UnderTheOldPlace = 8 * TerrainHeights.VerticesPerSide + 8;
    private const float Raised = 64f;
    private const float HeightTolerance = 16f;
    private const float AreaDistance = 64f;

    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName City = new("CityMod.esp");
    private static readonly PluginName Fence = new("FenceMod.esp");
    private static readonly PluginName Grass = new("GrassPatcher.esp");
    private static readonly PluginName Compat = new("CompatPatch.esp");
    private static readonly PluginName Cleaned = TestTargets.TargetName;
    private static readonly PluginName Patch = TestTargets.PatchName;

    private static readonly Vector3 A = new(-2000, 1024, 0);
    private static readonly Vector3 B = new(1024, 1024, 0);
    private static readonly Vector3 C = new(3000, 1024, 0);
    private static readonly Vector3 NextToB = B + new Vector3(30, 0, 0);
    private static readonly Vector3 FarFromB = C + new Vector3(0, 500, 0);

    private static readonly RecordKey Barrel = new(BaseGame, 0x10);
    private static readonly RecordKey Lamp = new(BaseGame, 0x11);
    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);
    private static readonly ExteriorCell Cell = new(TestTargets.Space, 0, 0);

    private static readonly WhatToCollect Plan = new(
        Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: MovedDistance, ProcessReplaced: false, JudgeOldPlaces: true, TerrainForChangedObjectsOnly: false);

    private static readonly ModTrustTable Mods = TestModTrust.Of(
        Cleaned, Patch, trusted: [BaseGame], untrusted: [City, Fence], neverTrusted: [Grass], needingCleanedMod: [Compat]);

    private static readonly TooCloseSettings Zone = new(
        BySize: false, Multiplier: 0f, ByDistance: true, RemovalDistance, ZoneShape.BoundingBox, NpcHandling.Never);

    private static readonly ChangedTerrainSettings TerrainOff = Terrain() with { On = false };

    private static readonly ModKey DataMod = ModKey.FromNameAndExtension("JudgeData.esp");

    private static readonly TestStatic Table = new(
        new FormKey(DataMod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    /// <summary>A model wide enough to reach a crate 100 units away within the removal distance, where the table's does not.</summary>
    private static readonly TestStatic Slab = new(
        new FormKey(DataMod, 0x702), @"test\slab.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-200, -200, 0), new Vector3(200, 200, 10))));

    private static readonly IBaseObjectShapes Shapes = TestVisibility.Over(TestShapes.Create(DataMod, "JudgeChangedObjectsData", Table, Slab));

    private static readonly PluginName[] Plugins = [BaseGame, City, Fence, Grass, Cleaned, Compat, Patch];

    private static readonly LoadOrderPlugins LoadOrder = TestLoadOrderPlugins.Of(
        new KnownPluginNames(Plugins), Patch, [.. Plugins.Select(plugin => new PluginListing(plugin, true, []))]);

    [Fact]
    public void TheCleanedModsObjectNextToTheOldPlaceCollides()
    {
        var neighbour = OwnObject(index: 0, NextToB);

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, neighbour);

        var collision = Assert.IsType<RemovalReason.TooClose>(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.Equal(ChangedObjectJudging.OldPlaceIdOf(judged.Collected.Targets.Single(target => target.Key == neighbour.Key)), collision.Other);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
    }

    [Theory]
    [InlineData("FenceMod.esp")]
    [InlineData("GrassPatcher.esp")]
    public void AModThatLoadsWithoutTheCleanedModDoesNotCollideWhateverItsTrust(string plugin)
    {
        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, Crate(new PluginName(plugin), NextToB));

        Assert.Equal(new OldPlaceCheck(judged.TargetOf(Barrel).Id, Collision: null, Failure: null), Assert.Single(judged.Result.OldPlaceChecks));
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void AModThatNeedsTheCleanedModCollidesEvenWhenItIsTrustedForIt()
    {
        var crate = Crate(Compat, NextToB);

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, crate);

        Assert.NotNull(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void TheEarlierPluginsOwnObjectDoesNotCollide()
    {
        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, Crate(City, NextToB));

        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    /// <summary>The barrel moved 20 units, so the cleaned mod's version of it lies inside the removal distance of its old place.</summary>
    [Fact]
    public void TheCleanedModsVersionOfTheCheckedObjectItselfIsNoCollision()
    {
        var judged = Judge(RestoreMode.RestoreIfNoCollision, [ChangedBarrel(earlierAt: B, cleanedModAt: NextToB)]);

        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void TheCleanedModsOtherChangedObjectNextToTheOldPlaceCollides()
    {
        var judged = Judge(RestoreMode.RestoreIfNoCollision, [ChangedBarrel(B, C), ChangedLamp(A, NextToB)]);

        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Lamp));
    }

    [Fact]
    public void TerrainTheCleanedModChangedAtTheOldPlaceCollides()
    {
        var terrain = new TerrainRecords(Flat(BaseGame), RaisedUnderTheOldPlace(Cleaned));

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, terrain);

        var collision = Assert.IsType<RemovalReason.TerrainChanged>(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.Equal(Cleaned, collision.ChangedBy);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
    }

    /// <summary>CityMod.esp raised the ground under the barrel's old place; it loads without the cleaned mod, so the ground is as it stood.</summary>
    [Fact]
    public void TheEarlierPluginsOwnTerrainDoesNotCollide()
    {
        var terrain = new TerrainRecords(Flat(BaseGame), RaisedUnderTheOldPlace(City));

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, terrain);

        Assert.Null(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void RestoreMapsEveryChangedObjectToItsEarlierVersionWithoutACheck()
    {
        var judged = Judge(RestoreMode.Restore, [ChangedBarrel(B, C), OwnObject(index: 0, NextToB)], rules: Unused());

        var restore = Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
        Assert.Equal((City, B), (restore.To.Plugin, restore.To.Placement.Position));
        Assert.Empty(judged.Result.OldPlaceChecks);
    }

    [Fact]
    public void RemoveMapsEveryObjectToDisableWithoutACheck()
    {
        var judged = Judge(RestoreMode.Remove, [ChangedBarrel(B, C), ChangedLamp(A, C), OwnObject(index: 0, NextToB)], rules: Unused());

        Assert.All(judged.Result.WhenRemoved, whenRemoved => Assert.IsType<WhenRemoved.Disable>(whenRemoved));
        Assert.Empty(judged.Result.OldPlaceChecks);
    }

    [Fact]
    public void RestoreIfNoCollisionRestoresTheFreeObjectAndDisablesTheCollidingOne()
    {
        var judged = Judge(RestoreMode.RestoreIfNoCollision, [ChangedBarrel(B, C), ChangedLamp(A, FarFromB), Crate(Compat, A + new Vector3(30, 0, 0))]);

        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Lamp));
        Assert.Equal(
            [judged.TargetOf(Barrel).Id, judged.TargetOf(Lamp).Id],
            judged.Result.OldPlaceChecks.Select(check => check.Target));
    }

    [Fact]
    public void TheCleanedModsOwnObjectsAreDisabledInEveryMode()
    {
        foreach (var mode in Enum.GetValues<RestoreMode>())
        {
            var judged = Judge(mode, [ChangedBarrel(B, C), OwnObject(index: 0, NextToB)]);

            Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(TestTargets.Key(0)));
        }
    }

    [Fact]
    public void AFailedCheckCountsAsACollisionAndIsRecorded()
    {
        var failing = new OldPlaceRules(
            _ => new FixedRule(new PlacementCheck(Clash: null, Failure: "mesh unreadable")), new FixedRule(new PlacementCheck(Clash: null, Failure: null)));

        var judged = Judge(RestoreMode.RestoreIfNoCollision, [ChangedBarrel(B, C)], rules: failing);

        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
        var failure = Assert.Single(judged.Result.Failures);
        Assert.Equal((JudgeChangedObjectsParts.OldPlace, "mesh unreadable"), (failure.Part, failure.Failure));
        Assert.Equal(judged.TargetOf(Barrel).Key.ToString(), failure.Subject!.Key);
    }

    [Fact]
    public void AMovedAndAReplacedObjectFollowTheirOwnModes()
    {
        var barrel = ChangedBarrel(B, B + new Vector3(3, 0, 0));
        var restyled = barrel with { Versions = barrel.Versions.SetItem(2, barrel.Winner with { Base = OtherBase }) };

        var judged = Judge(
            new ChangedObjectSettings(Moved: RestoreMode.Restore, Replaced: RestoreMode.Remove),
            [restyled, ChangedLamp(A, C)],
            Plan with { ProcessReplaced = true },
            rules: Unused());

        Assert.Equal(TargetOrigin.Replaced, judged.TargetOf(Barrel).Origin);
        Assert.Equal(TargetOrigin.Moved, judged.TargetOf(Lamp).Origin);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Lamp));
    }

    [Fact]
    public void TheResultDoesNotDependOnTheThreadCount()
    {
        PlacedRecordFact[] records = [ChangedBarrel(B, C), ChangedLamp(A, FarFromB), Crate(Compat, A + new Vector3(30, 0, 0)), Crate(Fence, NextToB)];

        var one = Judge(RestoreMode.RestoreIfNoCollision, records, threads: 1).Result;
        var many = Judge(RestoreMode.RestoreIfNoCollision, records, threads: 4).Result;

        Assert.Equal([typeof(WhenRemoved.Restore), typeof(WhenRemoved.Disable)], one.WhenRemoved.Select(whenRemoved => whenRemoved.GetType()));
        Assert.Equal(one.WhenRemoved.ToArray(), many.WhenRemoved.ToArray());
        Assert.Equal(one.OldPlaceChecks.ToArray(), many.OldPlaceChecks.ToArray());
    }

    [Fact]
    public void TheOldPlaceWorldHoldsTheCleanedModsObjectsFirstThenTheChangesWithoutIt()
    {
        PlacedRecordFact[] records = [ChangedBarrel(B, C), OwnObject(index: 0, NextToB), Crate(Fence, NextToB), Crate(Compat, NextToB)];
        var collected = Collect(records, Plan);

        var world = ChangedObjectJudging.CreateOldPlaceWorld(collected, Cleaned, Plan);

        Assert.Empty(world.Targets);
        Assert.Empty(world.SupportOnlyObjects);
        Assert.Equal(Enumerable.Range(0, world.OtherModObjects.Length), world.OtherModObjects.Select(other => other.Id.Index));
        var expectedKeys = collected.Targets.Select(target => target.Key).Append(CrateKey(Compat));
        Assert.Equal(expectedKeys, world.OtherModObjects.Select(other => other.Key));
        Assert.All(collected.Targets, target => Assert.Equal(target.Key, world.OtherModObjects[ChangedObjectJudging.OldPlaceIdOf(target).Index].Key));
    }

    /// <summary>The world answers "why can it cause removals" with the change in the world without the cleaned mod, for the cleaned mod's objects and for the others.</summary>
    [Fact]
    public void EveryObjectOfTheOldPlaceWorldIsChangedByItsChangeWithoutTheCleanedMod()
    {
        var collected = Collect([ChangedBarrel(B, C), OwnObject(index: 0, NextToB), Crate(Compat, NextToB), Crate(Fence, NextToB)], Plan);

        var world = ChangedObjectJudging.CreateOldPlaceWorld(collected, Cleaned, Plan);

        Assert.Equal(
            [new ObjectChange(Cleaned, AcceptedFrom: null), new ObjectChange(Cleaned, AcceptedFrom: null), new ObjectChange(Compat, AcceptedFrom: null)],
            world.OtherModObjects.Select(other => other.Change));
        Assert.All(world.OtherModObjects, other => Assert.Equal(other.Change, other.ChangeWithoutCleanedMod));
    }

    [Fact]
    public void AskingForTheOldPlaceWorldOfAScanThatDidNotJudgeTheOldPlacesThrows()
    {
        var notJudged = Plan with { JudgeOldPlaces = false };
        var collected = Collect([ChangedBarrel(B, C)], notJudged);

        Assert.Throws<InvalidOperationException>(() => ChangedObjectJudging.CreateOldPlaceWorld(collected, Cleaned, notJudged));
    }

    /// <summary>
    /// The cleaned mod gave the barrel another model without moving it. Its own model is as wide as a slab and would reach the crate
    /// that only exists for the cleaned mod; the model of the earlier version is the table's and stands clear of it.
    /// </summary>
    [Fact]
    public void AnEarlierVersionWithAnotherModelIsCheckedWithItsOwnModel()
    {
        var judged = JudgeReplacedBarrel(earlierModel: Table.Base, cleanedModsModel: Slab.Base);

        Assert.Equal(TargetOrigin.Replaced, judged.TargetOf(Barrel).Origin);
        Assert.Equal(new OldPlaceCheck(judged.TargetOf(Barrel).Id, Collision: null, Failure: null), Assert.Single(judged.Result.OldPlaceChecks));
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void AnEarlierVersionWithAWideModelCollidesWithTheCrateThatOnlyExistsForTheCleanedMod()
    {
        var judged = JudgeReplacedBarrel(earlierModel: Slab.Base, cleanedModsModel: Table.Base);

        Assert.IsType<RemovalReason.TooClose>(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
    }

    /// <summary>A batch group: the patch holds what an earlier patcher wrote, which no plugin vouches for, so it is never an accepted value.</summary>
    [Fact]
    public void AnObjectOnlyThePatchHoldsNextToTheOldPlaceCollides()
    {
        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, Crate(Patch, NextToB));

        Assert.IsType<RemovalReason.TooClose>(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.IsType<WhenRemoved.Disable>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void AnObjectThePatchPlacedWhereTheVersionBelowItAlreadyStoodDoesNotCollide()
    {
        var unchanged = Crate(BaseGame, NextToB) with { Versions = [Version(BaseGame, NextToB), Version(Patch, NextToB)] };

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, unchanged);

        Assert.Null(Assert.Single(judged.Result.OldPlaceChecks).Collision);
        Assert.IsType<WhenRemoved.Restore>(judged.WhenRemovedOf(Barrel));
    }

    [Fact]
    public void AnObjectThePatchMovedNextToTheOldPlaceCollides()
    {
        var movedByThePatch = Crate(BaseGame, FarFromB) with { Versions = [Version(BaseGame, FarFromB), Version(Patch, NextToB)] };

        var judged = JudgeBarrel(RestoreMode.RestoreIfNoCollision, movedByThePatch);

        Assert.IsType<RemovalReason.TooClose>(Assert.Single(judged.Result.OldPlaceChecks).Collision);
    }

    private sealed record Judged(CollectedObjects Collected, ChangedObjectJudgingResult Result)
    {
        public TargetObject TargetOf(RecordKey key) => Collected.Targets.Single(target => target.Key == key);

        public WhenRemoved WhenRemovedOf(RecordKey key) => Result.WhenRemoved[TargetOf(key).Id.Index];
    }

    private static Judged JudgeBarrel(RestoreMode mode, PlacedRecordFact around) => Judge(mode, [ChangedBarrel(B, C), around]);

    private static Judged JudgeBarrel(RestoreMode mode, TerrainRecords terrain) => Judge(mode, [ChangedBarrel(B, C)], terrain: terrain);

    private static Judged Judge(
        RestoreMode mode, PlacedRecordFact[] records, TerrainRecords? terrain = null, OldPlaceRules? rules = null, int threads = 1) =>
        Judge(new ChangedObjectSettings(mode, mode), records, Plan, terrain, rules, threads);

    private static Judged Judge(
        ChangedObjectSettings settings, PlacedRecordFact[] records, WhatToCollect plan, TerrainRecords? terrain = null, OldPlaceRules? rules = null, int threads = 1)
    {
        var collected = Collect(records, plan);
        var result = ChangedObjectJudging.Prepare(
            collected, rules ?? OldPlaceRulesOf(collected, plan, terrain), settings, new Execution(threads));
        return new Judged(collected, result);
    }

    private static Judged JudgeReplacedBarrel(BaseKey earlierModel, BaseKey cleanedModsModel)
    {
        var barrel = ChangedBarrel(B, B + new Vector3(3, 0, 0));
        var restyled = barrel with
        {
            Versions = barrel.Versions.SetItem(1, barrel.Versions[1] with { Base = earlierModel }).SetItem(2, barrel.Winner with { Base = cleanedModsModel }),
        };

        return Judge(
            new ChangedObjectSettings(Moved: RestoreMode.Remove, Replaced: RestoreMode.RestoreIfNoCollision),
            [restyled, Crate(Compat, B + new Vector3(100, 0, 0))],
            Plan with { ProcessReplaced = true });
    }

    /// <summary>The cleaned mod overrides every record that has a version of its own.</summary>
    private static CollectedObjects Collect(PlacedRecordFact[] records, WhatToCollect plan) =>
        PlacedObjects.Collect(
            new FixedFactsPlugin(
                [.. records], [], records.Where(record => record.Versions.Any(version => version.Plugin == Cleaned) && record.Key.Plugin != Cleaned).Select(record => record.Key).ToHashSet()),
            Mods,
            plan);

    /// <summary>The rules as the run builds them: causes of the old-place world, each checked object left out of its own causes, and the terrain of the world without the cleaned mod.</summary>
    private static OldPlaceRules OldPlaceRulesOf(CollectedObjects collected, WhatToCollect plan, TerrainRecords? terrain)
    {
        var world = ChangedObjectJudging.CreateOldPlaceWorld(collected, Cleaned, plan);
        var oldPlace = TestScenes.Create(world.Targets, world.OtherModObjects, Shapes);
        var visibleTargets = TestScenes.Create(collected.Targets, [], Shapes).VisibleTargets;
        var triangles = new TriangleStore(Shapes.ReadTriangles);
        return new OldPlaceRules(
            target => TooCloseObjects.CreatePlacementRule(
                collected.Targets,
                visibleTargets,
                RemovalCauseSelector.SelectForOldPlace(oldPlace.OtherModObjects, world.OtherModObjects.Length, ChangedObjectJudging.OldPlaceIdOf(target), Zone.Npcs),
                oldPlace.PlacedNpcs,
                Shapes,
                triangles,
                Zone),
            ChangedTerrainObjects.CreatePlacementRule(
                Mods.AcceptedWorldWithoutCleanedMod, LoadOrder, terrain ?? new TerrainRecords(), Shapes, terrain is null ? TerrainOff : Terrain()));
    }

    /// <summary>Rules that fail the test when asked: the mode decides without a check.</summary>
    private static OldPlaceRules Unused() => new(_ => new FixedRule(null), new FixedRule(null));

    private static PlacedRecordFact ChangedBarrel(Vector3 earlierAt, Vector3 cleanedModAt) => ChangedRecord(Barrel, earlierAt, cleanedModAt);

    private static PlacedRecordFact ChangedLamp(Vector3 earlierAt, Vector3 cleanedModAt) => ChangedRecord(Lamp, earlierAt, cleanedModAt);

    /// <summary>The base game puts it far away, CityMod.esp at <paramref name="earlierAt"/>, and the cleaned mod at <paramref name="cleanedModAt"/>.</summary>
    private static PlacedRecordFact ChangedRecord(RecordKey key, Vector3 earlierAt, Vector3 cleanedModAt)
    {
        PlacedVersionFact[] versions = [Version(BaseGame, A + new Vector3(0, 5000, 0)), Version(City, earlierAt), Version(Cleaned, cleanedModAt)];
        return TestPlacedRecords.Create(key, Cleaned, cleanedModAt, Table.Base) with { Versions = [.. versions] };
    }

    private static PlacedRecordFact OwnObject(int index, Vector3 position) => TestPlacedRecords.Create(TestTargets.Key(index), Cleaned, position, Table.Base);

    private static RecordKey CrateKey(PluginName plugin) => new(plugin, 0x900);

    private static PlacedRecordFact Crate(PluginName plugin, Vector3 position) => TestPlacedRecords.Create(CrateKey(plugin), plugin, position, Table.Base);

    private static PlacedVersionFact Version(PluginName plugin, Vector3 position) =>
        TestPlacedRecords.Version(plugin, new PlacementFacts(Shown, position, Vector3.Zero), Table.Base);

    private static readonly BaseKey OtherBase = new(new RecordKey(BaseGame, 0x101), BaseLinkKind.PlaceableObject);

    private static ChangedTerrainSettings Terrain() =>
        new(
            On: true, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: true, HeightTolerance, CountTerrainTypeChanges: false, 0,
            CountWaterHeightChanges: false, 0f, CountWaterAddedOrRemoved: false, 0, AreaBySize: false, 0f, AreaByDistance: true, AreaDistance);

    private static float[] FlatHeights(params (int Vertex, float Height)[] raised)
    {
        var heights = Enumerable.Repeat((float)Ground, TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide).ToArray();
        foreach (var (vertex, height) in raised) heights[vertex] = height;
        return heights;
    }

    private static TerrainVersion Flat(PluginName plugin) => new(plugin, FlatHeights(), Layers: null);

    private static TerrainVersion RaisedUnderTheOldPlace(PluginName plugin) => new(plugin, FlatHeights((UnderTheOldPlace, Raised)), Layers: null);

    /// <summary>A rule that answers every check the same way; null fails the test when asked.</summary>
    private sealed class FixedRule(PlacementCheck? check) : IPlacementRule
    {
        public IPlacementTries StartTries(TargetObject target) =>
            new Tries(check ?? throw new InvalidOperationException("The mode decides without an old-place check."));

        private sealed class Tries(PlacementCheck check) : IPlacementTries
        {
            public PlacementCheck Check(EulerPlacement placement) => check;

            public void Dispose()
            {
            }
        }
    }

    /// <summary>The terrain versions of the one test cell; only the test worldspace has land.</summary>
    private sealed class TerrainRecords(params TerrainVersion[] versions) : IPluginRecords
    {
        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => cell == Cell ? versions : [];

        public RecordKey? FindLandWorldspace(RecordKey space) => space == TestTargets.Space ? TestTargets.Space : null;

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => throw new NotSupportedException();

        public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

        public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
