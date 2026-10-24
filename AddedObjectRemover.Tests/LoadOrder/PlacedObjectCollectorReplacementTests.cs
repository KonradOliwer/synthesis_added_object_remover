using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// A record of another plugin that the target plugin overrides is skipped while the target plugin leaves it where the version
/// below it stood, and judged as a target object, with that version's placement, once the target plugin moved it.
/// </summary>
public class PlacedObjectCollectorReplacementTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName City = new("CityMod.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Target = TestTargets.TargetName;
    private static readonly PluginName Patch = TestTargets.PatchName;

    private static readonly RecordKey Tree = new(BaseGame, 0x10);
    private static readonly Vector3 Placed = new(500, 0, 0);
    private static readonly Vector3 Moved = new(600, 0, 0);
    private static readonly BaseKey TreeBase = new(new RecordKey(BaseGame, 0x100), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey OtherTreeBase = new(new RecordKey(BaseGame, 0x101), BaseLinkKind.PlaceableObject);
    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);

    private static readonly WhatToCollect Plan = new(SupportOnlyObjects: true, Terrain: false, Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(Target, Patch, trusted: [BaseGame], untrusted: [City, Later], trustedForOwnChanges: []);

    private static readonly PlacedRecordFact OwnObject = TestPlacedRecords.Create(TestTargets.Key(0), Target, Vector3.Zero);

    [Fact]
    public void AReplacementLeftWhereItStoodIsSkippedEvenWithAnotherModelRotationAndSize()
    {
        var restyled = At(Target, Placed + new Vector3(3, 0, 0)) with
        {
            Base = OtherTreeBase,
            Scale = 2f,
            Placement = new PlacementFacts(Shown, Placed + new Vector3(3, 0, 0), new Vector3(0, 0, 1f)),
        };

        var collected = Collect(Tree, At(BaseGame, Placed), restyled);

        Assert.Single(collected.Targets);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    [Fact]
    public void AReplacementMoved7UnitsIsUnmovedWithTheDefaultDistanceAndMovedWithADistanceOf5()
    {
        var nudged = At(Target, Placed + new Vector3(7, 0, 0));

        Assert.Single(Collect(Tree, At(BaseGame, Placed), nudged).Targets);
        Assert.Equal(2, Collect(Plan with { MovedDistance = 5f }, Tree, At(BaseGame, Placed), nudged).Targets.Length);
    }

    [Fact]
    public void AMovedReplacementIsATargetObjectWithThePlacementItReplaces()
    {
        var original = At(BaseGame, Placed) with { Scale = 1.5f, Placement = new PlacementFacts(Shown, Placed, new Vector3(0, 0, 0.5f)) };

        var replacement = Assert.Single(Collect(Tree, original, At(Target, Moved)).Targets, target => target.Key == Tree);

        Assert.Equal(Moved, replacement.Transform.Position);
        Assert.Equal(new ReplacedVersion(new EulerPlacement(Placed, new Vector3(0, 0, 0.5f), 1.5f), TestTargets.Space, CanMoveBack: true), replacement.Replaced);
    }

    [Fact]
    public void AReplacementMovedOutOfTheReplacedVersionsCellIsMovedBackIntoThatCell()
    {
        var gridCell = new RecordKey(BaseGame, 0x20);
        var original = At(BaseGame, Placed) with { Location = TestPlacedRecords.InTheSpace with { Cell = gridCell } };

        var replaced = ReplacementOf(Collect(Tree, original, At(Target, Moved))).Replaced!.Value;

        Assert.Equal((gridCell, true), (replaced.Cell, replaced.CanMoveBack));
    }

    [Fact]
    public void AReplacementMovedBetweenThePersistentAndTheTemporaryListIsNotMovedBack()
    {
        var persistent = At(BaseGame, Placed) with { Location = TestPlacedRecords.InTheSpace with { Cell = new RecordKey(BaseGame, 0x20), InPersistentList = true } };

        Assert.False(ReplacementOf(Collect(Tree, persistent, At(Target, Moved))).Replaced!.Value.CanMoveBack);
    }

    [Fact]
    public void AReplacementMovedIntoAnotherSpaceIsNotMovedBack()
    {
        var elsewhere = At(BaseGame, Placed) with { Location = new VersionLocation(new RecordKey(BaseGame, 0x30), new RecordKey(BaseGame, 0x30), InPersistentList: false) };

        Assert.False(ReplacementOf(Collect(Tree, elsewhere, At(Target, Moved))).Replaced!.Value.CanMoveBack);
    }

    [Fact]
    public void AReplacementThePatchHoldsInAnotherCellThanTheReplacedVersionIsNotMovedBack()
    {
        var original = At(BaseGame, Placed) with { Location = TestPlacedRecords.InTheSpace with { Cell = new RecordKey(BaseGame, 0x20) } };

        Assert.False(ReplacementOf(Collect(Tree, original, At(Target, Moved), At(Patch, Moved))).Replaced!.Value.CanMoveBack);
    }

    private static TargetObject ReplacementOf(CollectedObjects collected) => Assert.Single(collected.Targets, target => target.Key == Tree);

    [Fact]
    public void TheReplacedPlacementIsTheHighestVersionBelowTheTargetPlugin()
    {
        var replacement = Assert.Single(Collect(Tree, At(BaseGame, Vector3.Zero), At(City, Placed), At(Target, Moved)).Targets, target => target.Key == Tree);

        Assert.Equal(Placed, replacement.Replaced!.Value.Placement.Position);
    }

    [Fact]
    public void AMovedReplacementThatALaterPluginWinsIsSkipped()
    {
        var collected = Collect(Tree, At(BaseGame, Placed), At(Target, Moved), At(Later, Moved));

        Assert.Single(collected.Targets);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    [Fact]
    public void AMovedReplacementThePatchWinsIsJudgedWhereThePatchPutsIt()
    {
        var replacement = Assert.Single(Collect(Tree, At(BaseGame, Placed), At(Target, Moved), At(Patch, Moved * 2)).Targets, target => target.Key == Tree);

        Assert.Equal(Moved * 2, replacement.Transform.Position);
    }

    [Fact]
    public void AReplacementThePatchMovedBackIsSkipped() =>
        Assert.Single(Collect(Tree, At(BaseGame, Placed), At(Target, Moved), At(Patch, Placed)).Targets);

    [Fact]
    public void AReplacementOfARemovedVersionIsSkipped()
    {
        var removed = At(BaseGame, Placed) with { Placement = new PlacementFacts(Shown with { Deleted = true }, Placed, Vector3.Zero) };

        Assert.Single(Collect(Tree, removed, At(Target, Moved)).Targets);
    }

    [Fact]
    public void TheTargetPluginDoesNotStopTheWalk()
    {
        var plugin = new FixedFactsPlugin([OwnObject]);

        PlacedObjectCollector.Collect(plugin, Mods, Plan);

        Assert.Equal((false, true), (plugin.StopsTheWalk!(Target), plugin.StopsTheWalk!(BaseGame)));
    }

    private static PlacedVersionFact At(PluginName plugin, Vector3 position) =>
        new(plugin, new PlacementFacts(Shown, position, Vector3.Zero), Scale: null, TreeBase, TestPlacedRecords.InTheSpace);

    /// <param name="versions">Lowest first, the winner last.</param>
    private static CollectedObjects Collect(RecordKey key, params PlacedVersionFact[] versions) => Collect(Plan, key, versions);

    /// <param name="versions">Lowest first, the winner last.</param>
    private static CollectedObjects Collect(WhatToCollect plan, RecordKey key, params PlacedVersionFact[] versions)
    {
        var winner = versions[^1];
        var record = TestPlacedRecords.Create(key, winner.Plugin, winner.Placement.Position!.Value, winner.Base) with { Versions = [.. versions] };
        return PlacedObjectCollector.Collect(new FixedFactsPlugin(ImmutableArray.Create(OwnObject, record), [], new HashSet<RecordKey> { key }), Mods, plan);
    }
}
