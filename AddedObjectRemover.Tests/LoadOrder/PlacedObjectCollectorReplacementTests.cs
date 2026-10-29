using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// A record of another plugin that the target plugin overrides is skipped while the target plugin leaves it where the version
/// below it stood with the same base object. It is judged as a target object once the target plugin moved it (a moved object), or,
/// when replaced objects are processed, gave it another base object within the moved distance (a replaced object).
/// </summary>
public class PlacedObjectCollectorReplacementTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName City = new("CityMod.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName TrustedPatchOfAnotherMod = new("TrustedPatch.esp");
    private static readonly PluginName NeedsTarget = new("NeedsTarget.esp");
    private static readonly PluginName Target = TestTargets.TargetName;
    private static readonly PluginName Patch = TestTargets.PatchName;

    private static readonly RecordKey Tree = new(BaseGame, 0x10);
    private static readonly Vector3 Placed = new(500, 0, 0);
    private static readonly Vector3 Moved = new(600, 0, 0);
    private static readonly BaseKey TreeBase = new(new RecordKey(BaseGame, 0x100), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey OtherTreeBase = new(new RecordKey(BaseGame, 0x101), BaseLinkKind.PlaceableObject);
    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);

    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false, JudgeOldPlaces: false, TerrainForChangedObjectsOnly: false);
    private static readonly ModTrustTable Mods = TestModTrust.Of(
        Target, Patch, trusted: [BaseGame, TrustedPatchOfAnotherMod], untrusted: [City, Later], trustedForOwnChanges: [], needingCleanedMod: [NeedsTarget]);

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
    public void AMovedReplacementIsATargetObjectAtThePlacementTheTargetPluginGaveIt()
    {
        var original = At(BaseGame, Placed) with { Scale = 1.5f, Placement = new PlacementFacts(Shown, Placed, new Vector3(0, 0, 0.5f)) };

        var replacement = Assert.Single(Collect(Tree, original, At(Target, Moved)).Targets, target => target.Key == Tree);

        Assert.Equal(Moved, replacement.Transform.Position);
        Assert.Equal(TargetOrigin.Moved, replacement.Origin);
        Assert.Equal(2, Collect(Tree, original, At(Target, Moved)).Targets.Length);
    }

    /// <summary>The reader gives a position and a rotation together or not at all; a version without them cannot be put back.</summary>
    [Fact]
    public void AReplacementOfAVersionWithoutARotationIsSkipped()
    {
        var withoutRotation = At(BaseGame, Placed) with { Placement = new PlacementFacts(Shown, Placed, EulerRotation: null) };

        Assert.Single(Collect(Tree, withoutRotation, At(Target, Moved)).Targets);
    }

    [Fact]
    public void AReplacementMovedExactlyTheMovedDistanceIsUnmoved()
    {
        var atTheDistance = At(Target, Placed + new Vector3(Plan.MovedDistance, 0, 0));

        Assert.Single(Collect(Tree, At(BaseGame, Placed), atTheDistance).Targets);
        Assert.Equal(2, Collect(Plan with { MovedDistance = Plan.MovedDistance - 0.5f }, Tree, At(BaseGame, Placed), atTheDistance).Targets.Length);
    }

    [Fact]
    public void TheTargetPluginsOwnObjectIsNoMovedReplacement() =>
        Assert.Equal(TargetOrigin.CleanedMod, Assert.Single(Collect(Tree, At(BaseGame, Placed), At(Target, Moved)).Targets, target => target.Key == OwnObject.Key).Origin);

    [Fact]
    public void TheReplacedVersionIsTheHighestVersionBelowTheTargetPlugin()
    {
        var nearTheCityVersion = At(Target, Placed + new Vector3(3, 0, 0));

        Assert.Single(Collect(Tree, At(BaseGame, Vector3.Zero), At(City, Placed), nearTheCityVersion).Targets);
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
    public void AReplacementThePatchPutBackWhereItStoodIsSkipped() =>
        Assert.Single(Collect(Tree, At(BaseGame, Placed), At(Target, Moved), At(Patch, Placed)).Targets);

    [Fact]
    public void AReplacementOfARemovedVersionIsSkipped()
    {
        var removed = At(BaseGame, Placed) with { Placement = new PlacementFacts(Shown with { Deleted = true }, Placed, Vector3.Zero) };

        Assert.Single(Collect(Tree, removed, At(Target, Moved)).Targets);
    }

    [Fact]
    public void TheTargetPluginsOwnObjectHasNoEarlierVersion() =>
        Assert.Null(Assert.Single(Collect(Tree, At(BaseGame, Placed), At(Target, Moved)).Targets, target => target.Key == OwnObject.Key).EarlierVersion);

    /// <summary>The base game puts the tree at (0, 0, 0), the city mod below the target plugin at (500, 0, 0) turned and scaled, and the target plugin at (600, 0, 0).</summary>
    [Fact]
    public void AMovedObjectsEarlierVersionIsTheVersionOfThePluginJustBelowTheTargetPlugin()
    {
        var turned = new Vector3(0, 0, 0.5f);
        var cityVersion = At(City, Placed) with { Scale = 1.5f, Placement = new PlacementFacts(Shown, Placed, turned) };

        var earlier = EarlierVersionOf(Collect(Tree, At(BaseGame, Vector3.Zero), cityVersion, At(Target, Moved)));

        Assert.Equal((City, new EulerPlacement(Placed, turned, 1.5f), 1.5f, TreeBase), (earlier.Plugin, earlier.Placement, earlier.StoredScale, earlier.Base));
    }

    [Fact]
    public void AnEarlierVersionWithoutAStoredScaleHasTheScaleOne()
    {
        var earlier = EarlierVersionOf(Collect(Tree, At(BaseGame, Placed), At(Target, Moved)));

        Assert.Equal(((float?)null, 1f), (earlier.StoredScale, earlier.Placement.Scale));
    }

    [Fact]
    public void AnEarlierVersionKeepsItsPluginsCellAndPersistentFlag()
    {
        var inAnotherCell = At(BaseGame, Placed) with { Cell = new CellFact(TestTargets.SpaceKey(0x200), "Elsewhere"), IsPersistent = true };

        var earlier = EarlierVersionOf(Collect(Tree, inAnotherCell, At(Target, Moved)));

        Assert.Equal((new CellFact(TestTargets.SpaceKey(0x200), "Elsewhere"), true), (earlier.Cell, earlier.IsPersistent));
    }

    /// <summary>The target plugin moves the object into the space of the test target; its earlier version stood in an interior or in another worldspace.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnObjectWhoseEarlierVersionStoodInAnotherSpaceIsNotAChangedObject(bool earlierInInterior)
    {
        var otherSpace = TestTargets.SpaceKey(0x200);
        var earlierCell = earlierInInterior ? otherSpace : TestTargets.SpaceKey(0x201);
        var elsewhere = At(BaseGame, Placed) with { Cell = new CellFact(earlierCell, null), SpaceKey = otherSpace };

        var collected = Collect(Tree, elsewhere, At(Target, Moved));

        Assert.DoesNotContain(collected.Targets, target => target.Key == Tree);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    /// <summary>The earlier version stays the one below the target plugin when the patch, holding what an earlier patcher of the group wrote, wins above it.</summary>
    [Fact]
    public void AMovedObjectsEarlierVersionIsTheSameWhenThePatchWinsAboveTheTargetPlugin()
    {
        var earlier = EarlierVersionOf(Collect(Tree, At(BaseGame, Vector3.Zero), At(City, Placed), At(Target, Moved), At(Patch, Moved * 2)));

        Assert.Equal((City, Placed), (earlier.Plugin, earlier.Placement.Position));
    }

    [Fact]
    public void AMovedObjectWithTheModelOfItsEarlierVersionRestoresOnlyThePlacement() =>
        Assert.False(EarlierVersionOf(Collect(Tree, At(BaseGame, Placed), At(Target, Moved))).RestoresWholeRecord);

    /// <summary>The earlier version's own model comes back with the whole record of that plugin.</summary>
    [Fact]
    public void AnEarlierVersionWithAnotherModelRestoresTheWholeRecord()
    {
        var restyled = At(Target, Moved) with { Base = OtherTreeBase };

        var earlier = EarlierVersionOf(Collect(Tree, At(BaseGame, Placed), restyled));

        Assert.Equal((true, TreeBase, BaseGame), (earlier.RestoresWholeRecord, earlier.Base, earlier.Plugin));
    }

    [Fact]
    public void AnObjectGivenAnotherModelWithinTheMovedDistanceIsAReplacedObjectWhenReplacedObjectsAreProcessed()
    {
        var restyled = At(Target, Placed + new Vector3(3, 0, 0)) with { Base = OtherTreeBase };

        var collected = Collect(Plan with { ProcessReplaced = true }, Tree, At(BaseGame, Placed), restyled);

        var replaced = Assert.Single(collected.Targets, target => target.Key == Tree);
        Assert.Equal(TargetOrigin.Replaced, replaced.Origin);
        Assert.Equal(Placed + new Vector3(3, 0, 0), replaced.Transform.Position);
        var earlier = EarlierVersionOf(collected);
        Assert.Equal((BaseGame, TreeBase, true), (earlier.Plugin, earlier.Base, earlier.RestoresWholeRecord));
        Assert.Equal(0, collected.Counts.OthersOverriddenByTarget);
    }

    [Fact]
    public void AnObjectGivenAnotherModelWithinTheMovedDistanceIsSkippedWhenReplacedObjectsAreNotProcessed()
    {
        var restyled = At(Target, Placed + new Vector3(3, 0, 0)) with { Base = OtherTreeBase };

        var collected = Collect(Plan, Tree, At(BaseGame, Placed), restyled);

        Assert.DoesNotContain(collected.Targets, target => target.Key == Tree);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    [Fact]
    public void AnObjectBothMovedAndGivenAnotherModelIsAMovedObjectWhateverReplacedObjectsDo()
    {
        var restyled = At(Target, Moved) with { Base = OtherTreeBase };

        var origins = new[] { Plan, Plan with { ProcessReplaced = true } }
            .Select(plan => Assert.Single(Collect(plan, Tree, At(BaseGame, Placed), restyled).Targets, target => target.Key == Tree).Origin);

        Assert.Equal([TargetOrigin.Moved, TargetOrigin.Moved], origins);
    }

    [Fact]
    public void AnObjectKeepingItsModelWithinTheMovedDistanceIsSkippedWhenReplacedObjectsAreProcessed()
    {
        var turned = At(Target, Placed + new Vector3(3, 0, 0));

        var collected = Collect(Plan with { ProcessReplaced = true }, Tree, At(BaseGame, Placed), turned);

        Assert.DoesNotContain(collected.Targets, target => target.Key == Tree);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    /// <summary>After a whole-record restore the patch holds the earlier base at the old place, so a later run finds nothing to judge.</summary>
    [Fact]
    public void AnObjectThePatchPutBackWithItsEarlierModelIsSkippedWhenReplacedObjectsAreProcessed()
    {
        var restyled = At(Target, Placed) with { Base = OtherTreeBase };

        var collected = Collect(Plan with { ProcessReplaced = true }, Tree, At(BaseGame, Placed), restyled, At(Patch, Placed));

        Assert.DoesNotContain(collected.Targets, target => target.Key == Tree);
        Assert.Equal(1, collected.Counts.OthersOverriddenByTarget);
    }

    /// <summary>
    /// The answer must not depend on how far the scan walked below the cleaned mod's version, which depends on whether the old
    /// places are judged. A trusted plugin between the cleaned mod's version and the winner decides the record, as a later plugin does.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ATrustedPluginBetweenTheTargetPluginAndThePatchLeavesTheRecordUnjudgedWhetherOrNotItNeedsTheTargetPlugin(bool judgeOldPlaces, bool needsTarget)
    {
        var between = needsTarget ? NeedsTarget : TrustedPatchOfAnotherMod;
        var plan = Plan with { JudgeOldPlaces = judgeOldPlaces };

        var collected = Collect(plan, Tree, At(BaseGame, Placed), At(Target, Moved), At(between, Moved), At(Patch, Moved * 2));

        Assert.DoesNotContain(collected.Targets, target => target.Key == Tree);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTargetPluginsVersionDirectlyBelowThePatchIsStillJudgedWhetherOrNotTheOldPlacesAreJudged(bool judgeOldPlaces)
    {
        var collected = Collect(Plan with { JudgeOldPlaces = judgeOldPlaces }, Tree, At(BaseGame, Placed), At(Target, Moved), At(Patch, Moved * 2));

        Assert.Contains(collected.Targets, target => target.Key == Tree);
    }

    /// <summary>
    /// The base game has the object at A, the target plugin at B, another cleaned mod of the group at C, and the patch, holding what
    /// that mod's run restored, at B again: the target plugin's run compares B with A.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnObjectAnotherCleanedModOfTheGroupChangedAboveTheTargetPluginIsJudgedAgainstTheVersionBelowTheTargetPlugin(bool judgeOldPlaces)
    {
        var collected = Collect(Plan with { JudgeOldPlaces = judgeOldPlaces }, Tree, At(BaseGame, Placed), At(Target, Moved), At(City, Moved * 2), At(Patch, Moved));

        var judged = Assert.Single(collected.Targets, target => target.Key == Tree);
        Assert.Equal((Moved, BaseGame), (judged.Transform.Position, EarlierVersionOf(collected).Plugin));
    }

    [Fact]
    public void AnEarlierVersionKeepsWhatItsPluginSaysAboutTheReferenceItself()
    {
        var marker = TestPlacedRecords.PlainReference with { HasMapMarker = true, ReferenceRadius = 64f };

        var earlier = EarlierVersionOf(Collect(Tree, At(BaseGame, Placed) with { Reference = marker }, At(Target, Moved)));

        Assert.Equal(marker, earlier.Reference);
    }

    [Fact]
    public void TheTargetPluginDoesNotStopTheWalk()
    {
        var plugin = new FixedFactsPlugin([OwnObject]);

        PlacedObjects.Collect(plugin, Mods, Plan);

        Assert.Equal((false, true), (plugin.StopsTheWalk!(Target), plugin.StopsTheWalk!(BaseGame)));
    }

    private static EarlierVersion EarlierVersionOf(CollectedObjects collected) =>
        Assert.Single(collected.Targets, target => target.Key == Tree).EarlierVersion ?? throw new InvalidOperationException("The target has no earlier version.");

    private static PlacedVersionFact At(PluginName plugin, Vector3 position) =>
        TestPlacedRecords.Version(plugin, new PlacementFacts(Shown, position, Vector3.Zero), TreeBase);

    /// <param name="versions">Lowest first, the winner last.</param>
    private static CollectedObjects Collect(RecordKey key, params PlacedVersionFact[] versions) => Collect(Plan, key, versions);

    /// <param name="versions">Lowest first, the winner last.</param>
    private static CollectedObjects Collect(WhatToCollect plan, RecordKey key, params PlacedVersionFact[] versions)
    {
        var winner = versions[^1];
        var record = TestPlacedRecords.Create(key, winner.Plugin, winner.Placement.Position!.Value, winner.Base) with { Versions = [.. versions] };
        return PlacedObjects.Collect(new FixedFactsPlugin(ImmutableArray.Create(OwnObject, record), [], new HashSet<RecordKey> { key }), Mods, plan);
    }
}
