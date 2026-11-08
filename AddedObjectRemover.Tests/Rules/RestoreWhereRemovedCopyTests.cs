using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The copies restoring where the cleaned mod was removed leaves of a moved rock beside the standing campfire of a camp: when a copy
/// is planned, when it is refused, and what the left-behind steps measure around it.
/// </summary>
public sealed class RestoreWhereRemovedCopyTests
{
    /// <summary>A base-game rock the cleaned mod moved from a cleared place into the ring around its campfire, whose camp still stands, goes back with a copy left in the ring.</summary>
    [Fact]
    public void AMovedRockAroundAStandingCampfireGoesBackWithACopy()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery).Objects);

        Assert.Equal(new CloneJudged(RefusedBy: null), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>The camp is cleared but its campfire stands, and the visible left-behind step measures the rock around it.</summary>
    [Fact]
    public void AMovedRockBesideTheStandingCampfireOfAClearedCampGoesBackWithACopy()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround).Objects);

        Assert.Equal(AreaVerdict.Cleared, judged.WhereItIsNow!.Verdict);
        Assert.Equal(new CloneJudged(RefusedBy: null), judged.Cloning);
        Assert.True(judged.IsRestored);
    }

    /// <summary>The same cleared camp without the left-behind step, or with the campfire removed too: nothing holds the rock, so it goes back as before.</summary>
    [Theory]
    [InlineData(false, new[] { 0, 3, 4 })]
    [InlineData(true, new[] { 0, 1, 3, 4 })]
    public void AMovedRockThatNoStandingObjectHoldsGoesBackWithoutACopy(bool leftBehindOn, int[] removed)
    {
        var judged = Assert.Single(Plan(CampScene(), removed, [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: leftBehindOn ? LookAround : null).Objects);

        Assert.Equal(((CloneJudged?)null, true), (judged.Cloning, judged.IsRestored));
    }

    /// <summary>The standing campfire of the cleared camp needs a copy of the rock, but the rock may not be copied, so it goes back without one.</summary>
    [Fact]
    public void AMovedRockBesideTheStandingCampfireThatMayNotBeCopiedGoesBackWithoutACopy()
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: TestBaseFacts.Unresolved, visibleLeftBehind: LookAround).Objects);

        Assert.Equal(new CloneJudged(CloneRefusal.NotPlainScenery), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null, (PlacedRefusal?)null), (judged.IsRestored, judged.KeptForArea, judged.Refused));
        Assert.Null(RestoreOutcomes.KeptBecause(judged, key => key.ToString()));
    }

    /// <summary>The copy's check stops in one pass and a guard refuses the record in a later one: both stops are reported.</summary>
    [Fact]
    public void ARecordRefusedAfterItsCopysCheckStoppedReportsBothStops()
    {
        var planned = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery).Objects);
        var refusal = new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, By: null, Failure: "guard broke");
        var lost = planned.Cloning! with { RefusedBy = CloneRefusal.ActorsWalkOnIt, Failure = "copy check broke" };

        var outcome = PlacedJudgments.WithOutcomes([planned], [planned.Change], [refusal], [lost]);

        Assert.Equal(
            [(RestoreWhereRemovedParts.ObjectCheck, "guard broke"), (RestoreWhereRemovedParts.ObjectCheck, "copy check broke")],
            PlacedJudgments.FailedChecks(outcome).Select(failure => (failure.Part, failure.Failure)));
        Assert.Equal((false, (CloneRefusal?)CloneRefusal.ActorsWalkOnIt, (CloneJudged?)null), (outcome[0].IsRestored, outcome[0].Cloning!.RefusedBy, RestoreOutcomes.RefusedCopy(outcome[0])));
    }

    public static TheoryData<string, CloneRefusal> RefusedCopies => new()
    {
        { "not plain scenery, such as a door", CloneRefusal.NotPlainScenery },
        { "base not in the load order", CloneRefusal.NotPlainScenery },
        { "base with distant LOD", CloneRefusal.HasDistantLod },
        { "visible when distant", CloneRefusal.HasDistantLod },
        { "persistent", CloneRefusal.Persistent },
        { "scripted, linked or with an enable parent", CloneRefusal.HoldsMoreThanPlacement },
    };

    [Theory]
    [MemberData(nameof(RefusedCopies))]
    public void AMovedRockAroundAStandingCampfireThatMayNotBeCopiedGoesBackWithoutACopy(string rock, CloneRefusal expected)
    {
        var (change, bases) = rock switch
        {
            "not plain scenery, such as a door" => (MovedRock(), new SceneryBases(Plain: false, DistantLod: false)),
            "base not in the load order" => (MovedRock(), TestBaseFacts.Unresolved),
            "base with distant LOD" => (MovedRock(), new SceneryBases(Plain: true, DistantLod: true)),
            "visible when distant" => (MovedRock() with { ReferenceNow = TestPlacedRecords.PlainReference with { IsVisibleWhenDistant = true } }, PlainScenery),
            "persistent" => (MovedRock() with { IsPersistent = true }, PlainScenery),
            _ => (MovedRock() with { ReferenceNow = TestPlacedRecords.PlainReference with { HoldsOnlyPlacement = false } }, PlainScenery),
        };

        var judged = Assert.Single(Plan(CampScene(), removed: [0], [change], bases: bases).Objects);

        Assert.Equal(new CloneJudged(expected), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    public static TheoryData<string> RocksWithoutCopyRule => ["base swap in place", "turned only", "rescaled only", "position held by a later plugin", "copy exists", "linked from the cleaned mod", "old place stands"];

    [Theory]
    [MemberData(nameof(RocksWithoutCopyRule))]
    public void TheCopyRuleDoesNotApplyToARockThatWasNotMovedOrWhoseOldPlaceStaysOrThatHasACopy(string rock)
    {
        var rockKey = MovedRock().Key;
        var (targets, change, removed) = rock switch
        {
            "base swap in place" => (CampScene(TargetOrigin.Replaced), MovedRock() with { Earlier = MovedRock().Earlier with { Undo = PlacedParts.BaseAndData | PlacedParts.Position } }, new[] { 0 }),
            "turned only" => (CampScene(), MovedRock(undo: PlacedParts.Rotation), new[] { 0 }),
            "rescaled only" => (CampScene(), MovedRock(undo: PlacedParts.Scale), new[] { 0 }),
            "position held by a later plugin" => (CampScene(), MovedRock(undo: PlacedParts.BaseAndData), new[] { 0 }),
            "copy exists" => (CampScene(Own(5, new Vector3(-3000, 0, 0)) with { CloneOf = rockKey }), MovedRock(), new[] { 0 }),
            "linked from the cleaned mod" => (CampScene(), MovedRock() with { LinkedFromCleanedMod = true }, new[] { 0 }),
            _ => (CampScene(), MovedRock(), Array.Empty<int>()),
        };

        var judged = Assert.Single(Plan(targets, removed, [change], bases: PlainScenery).Objects);

        Assert.Null(judged.Cloning);
    }

    /// <summary>
    /// A later mod's block stands 60 units from the centre of the rock beside the standing campfire. A zone reaches the rock's half size,
    /// 20 units, plus the too-close distance: 30 units by the moved-object settings, 70 or 50 by the cleaned mod's own, which judge the copy.
    /// </summary>
    [Theory]
    [InlineData(50f, true)]
    [InlineData(30f, false)]
    public void ACopyTooCloseToALaterModsObjectByTheCleanedModsOwnSettingsIsRefused(float ownDistance, bool refused)
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, others: [LaterBlock(Camp + new Vector3(-60, 0, 0))], cleanedModsObjects: OwnTooClose(ownDistance)).Objects);

        Assert.Equal(new CloneJudged(refused ? CloneRefusal.TooCloseAsCleanedModsObject : null), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>In the cleared camp, the copy the standing campfire needs is too close to a later mod's block by the cleaned mod's own settings, so the rock goes back without a copy.</summary>
    [Fact]
    public void AMovedRockBesideTheStandingCampfireWhoseCopyIsTooCloseAsTheCleanedModsObjectGoesBackWithoutACopy()
    {
        var judged = Assert.Single(
            Plan(CampScene(), removed: [0, 3, 4], [MovedRock()], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround, others: [LaterBlock(Camp + new Vector3(-60, 0, 0))], cleanedModsObjects: OwnTooClose(50f)).Objects);

        Assert.Equal(new CloneJudged(CloneRefusal.TooCloseAsCleanedModsObject), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null, (PlacedRefusal?)null), (judged.IsRestored, judged.KeptForArea, judged.Refused));
        Assert.Null(RestoreOutcomes.KeptBecause(judged, key => key.ToString()));
    }

    /// <summary>
    /// In the cleared camp a second rock goes back to 60 units from the copy's centre and keeps a scale Later.esp set. Whether or not
    /// the removal steps believe Later.esp, the next patcher reads the rock the patch put back as support only, never as a cause, so
    /// the copy stays.
    /// </summary>
    [Theory]
    [InlineData(VersionTrust.None)]
    [InlineData(VersionTrust.Full)]
    public void ACopyBesideARockGoingBackStaysWhateverTheTrustOfThePartsTheRockKeeps(VersionTrust laterTrust)
    {
        var second = ChangedRock(0x901, Camp + new Vector3(-60, 0, 0));
        var keepingScale = second with { BaseNow = Block.Base, Earlier = second.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };

        var result = Plan(
            CampScene(), DecisionsRemoving(CampScene(), 0, 3, 4), [MovedRock(), keepingScale], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround,
            cleanedModsObjects: OwnTooClose(50f), laterTrust: laterTrust);

        Assert.Equal(new CloneJudged(RefusedBy: null), result.Objects[0].Cloning);
    }

    /// <summary>
    /// The changed-terrain rule is asked about the copy with the cleaned mod's own settings, as the cleaned mod's object where the rock
    /// stands now, over the load order as read; a finding or a stopped check refuses it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACopyTheCleanedModsOwnChangedTerrainCheckFindsOrStopsOnIsRefused(bool stops)
    {
        var terrain = new TestTerrainJudging(
            stops ? new TerrainCheckOutcome(Finding: null, "InvalidOperationException: broke") : new TerrainCheckOutcome(new ObjectOnChangedTerrain(new TargetId(0), Later, TerrainChangeKind.Height, []), Failure: null));

        var result = Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, changedTerrainRule: terrain);

        var judged = Assert.Single(result.Objects);
        Assert.Equal(new CloneJudged(CloneRefusal.OnChangedTerrainAsCleanedModsObject, stops ? "InvalidOperationException: broke" : null), judged.Cloning);
        Assert.Equal([(Camp, TargetOrigin.CleanedMod, (RecordKey?)MovedRock().Key)], terrain.Checked.Select(asked => (asked.Transform.Position, asked.Origin, asked.CloneOf)).Distinct());
        Assert.Equal(stops ? [(RestoreWhereRemovedParts.CopyCheck, "InvalidOperationException: broke")] : [], result.Failures.Select(failure => (failure.Part, failure.Failure)));
    }

    /// <summary>
    /// In the cleared camp rock A goes back with a copy the standing campfire needs. Rock B, which the cleaned mod moved into a second
    /// camp that still stands, goes back with a copy too, to 60 units from A's copy, and keeps a scale Later.esp set. Whether or not
    /// the removal steps believe Later.esp, the next patcher reads B as support only, so both go back with their copies.
    /// </summary>
    [Theory]
    [InlineData(VersionTrust.None)]
    [InlineData(VersionTrust.Full)]
    public void ARockGoingBackWithACopyIsNoCauseForAnotherCopy(VersionTrust laterTrust)
    {
        var secondCamp = new Vector3(5000, 5000, 0);
        var secondRockKey = new RecordKey(BaseGame, 0x902);
        var targets = CampScene(
            Own(5, secondCamp) with { Origin = TargetOrigin.Moved, Key = secondRockKey },
            Own(6, secondCamp + new Vector3(60, 0, 0)),
            Own(7, secondCamp + new Vector3(0, 150, 0)),
            Own(8, secondCamp + new Vector3(0, -150, 0)));
        var second = ChangedRock(secondRockKey.Id, Camp + new Vector3(-60, 0, 0), target: new TargetId(5));
        var keepingScale = second with { Earlier = second.Earlier with { LeftToLaterPlugins = ImmutableSortedDictionary<PlacedParts, PluginName>.Empty.Add(PlacedParts.Scale, Later) } };

        var result = Plan(
            targets, DecisionsRemoving(targets, 0, 3, 4), [MovedRock(), keepingScale], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: LookAround,
            cleanedModsObjects: OwnTooClose(50f), laterTrust: laterTrust);

        Assert.Equal((new CloneJudged(RefusedBy: null), true), (result.Objects[0].Cloning, result.Objects[0].IsRestored));
        Assert.Equal((new CloneJudged(RefusedBy: null), true), (result.Objects[1].Cloning, result.Objects[1].IsRestored));
    }

    /// <summary>
    /// A navmesh the cleaned mod added, which stays changed, has a triangle whose centre lies inside the rock in the camp ring, so
    /// actors would walk on a copy: the copy is lost and the rock stays for its area. A navmesh beside the rock leaves the copy.
    /// </summary>
    [Theory]
    [InlineData(0f, true)]
    [InlineData(100f, false)]
    public void ARockWhoseCopyActorsWouldWalkOnGoesBackWithoutACopy(float navmeshOffset, bool walkedOn)
    {
        var judged = Assert.Single(Plan(CampScene(), removed: [0], [MovedRock()], bases: PlainScenery, navmeshes: [AddedNavmeshAround(Camp + new Vector3(0, navmeshOffset, 0))]).Objects);

        Assert.Equal(new CloneJudged(walkedOn ? CloneRefusal.ActorsWalkOnIt : null), judged.Cloning);
        Assert.Equal((true, (AreaVerdict?)null), (judged.IsRestored, judged.KeptForArea));
    }

    /// <summary>
    /// In the cleared camp the rock goes back with a copy the campfire needs, and a second base game rock goes back to 30 units from
    /// the rock's place: the copy stands there, so the second rock is refused. Without the left-behind step no copy is planned, the
    /// rock leaves its place and the second rock goes back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoingBackIntoTheCopysPlaceIsRefused(bool leftBehindOn)
    {
        var second = ChangedRock(0x901, Camp + new Vector3(-30, 0, 0)) with { BaseNow = Block.Base };

        var result = Plan(CampScene(), removed: [0, 3, 4], [MovedRock(), second], settings: HalfMayStand, bases: PlainScenery, visibleLeftBehind: leftBehindOn ? LookAround : null);

        Assert.Equal((leftBehindOn ? new CloneJudged(RefusedBy: null) : null, true), (result.Objects[0].Cloning, result.Objects[0].IsRestored));
        Assert.Equal(leftBehindOn ? new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, RockKey, Failure: null) : null, result.Objects[1].Refused);
    }

    [Fact]
    public void ALeftBehindStepThatIsSwitchedOffMeasuresNoNeighbours()
    {
        var targets = CampScene();
        var decisions = DecisionsRemoving(targets);
        var caches = CachesOf(targets);
        List<TargetId> visible = [new TargetId(99)];
        List<TargetId> invisible = [new TargetId(99)];

        LeftBehindRules.CreateVisibleNeighbours(targets, decisions, caches, settings: null).Around(targets[1], visible);
        LeftBehindRules.CreateInvisibleNeighbours(targets, decisions, caches, settings: null).Around(targets[1], invisible);

        Assert.Equal((0, 0), (visible.Count, invisible.Count));
    }

    /// <summary>The visible step measures the visible targets around a visible one; the invisible step those around an invisible one, each only around its own kind.</summary>
    [Fact]
    public void EachLeftBehindStepMeasuresTheVisibleTargetsAroundAnObjectOfItsKind()
    {
        var marker = TestVisibility.TaggedBase(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
        var targets = CampScene(TestTargets.Create(5, TestTargets.At(Camp + new Vector3(-60, 0, 0)), marker, TestTargets.Space));
        var decisions = DecisionsRemoving(targets);
        var caches = CachesOf(targets, PlainScenery);
        var visibleStep = LeftBehindRules.CreateVisibleNeighbours(targets, decisions, caches, LookAround);
        var invisibleStep = LeftBehindRules.CreateInvisibleNeighbours(targets, decisions, caches, new InvisibleLeftBehindSettings(LookAround.Surroundings));

        Assert.Equal([2, 3, 4], NeighbourIndexes(visibleStep, targets[1]));
        Assert.Empty(NeighbourIndexes(visibleStep, targets[5]));
        Assert.Equal([1, 2, 3, 4], NeighbourIndexes(invisibleStep, targets[5]));
        Assert.Empty(NeighbourIndexes(invisibleStep, targets[1]));
    }

    private static int[] NeighbourIndexes(ILeftBehindNeighbours step, TargetObject target)
    {
        List<TargetId> neighbours = [];
        step.Around(target, neighbours);
        return [.. neighbours.Select(neighbour => neighbour.Index)];
    }

    private sealed class SceneryBases(bool Plain, bool DistantLod) : IBaseFacts
    {
        public BaseFacts Of(BaseKey baseKey) => BaseFacts.Unresolved(baseKey.Record) with { Resolved = true, IsPlainScenery = Plain, HasDistantLod = DistantLod };
    }
}
