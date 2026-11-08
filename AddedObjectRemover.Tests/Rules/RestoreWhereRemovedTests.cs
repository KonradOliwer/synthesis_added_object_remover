using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The area rule of restoring where the cleaned mod was removed: the cleaned mod's own visible objects in a column around a judged
/// point, their fates and the share left standing, and where a moved object is judged, over in-memory target objects.
/// </summary>
public sealed class RestoreWhereRemovedTests
{
    private const float OutsideTheArea = 1500f;

    [Fact]
    public void AChangeWhoseAreaIsClearedOfTheCleanedModsObjectsGoesBack()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0, 1, 2], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 0, AreaVerdict.Cleared), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Verdict));
        Assert.Equal(3 * BlockVolume, judged.JudgedPlace.Size);
        Assert.True(judged.IsRestored);
    }

    [Fact]
    public void AChangeWhereOneRemovedPieceStandsAmongKeptOnesStays()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0))];

        var judged = Assert.Single(Plan(targets, removed: [1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 2, AreaVerdict.StillStanding), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    [Fact]
    public void ALoneRemovedPieceLeavesNothingStandingSoItsChangeGoesBack()
    {
        var judged = Assert.Single(Plan([Own(0, new Vector3(300, 0, 0))], removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, 0, AreaVerdict.Cleared), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Verdict));
        Assert.True(judged.IsRestored);
    }

    /// <summary>The cleaned mod's only object stands outside the area, and another plugin's object it moved stands inside, which does not count.</summary>
    [Fact]
    public void AChangeWithNoObjectOfTheCleanedModAroundStays()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(OutsideTheArea, 0, 0)), Own(1, new Vector3(50, 0, 0)) with { Origin = TargetOrigin.Moved }];

        var judged = Assert.Single(Plan(targets, removed: [0, 1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((0, AreaVerdict.NoObjectsAround), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    [Fact]
    public void TheAreaIsAColumnSoAnObjectFarAboveTheOldPlaceCounts()
    {
        var judged = Assert.Single(Plan([Own(0, new Vector3(0, 0, 20000))], removed: [], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((1, AreaVerdict.StillStanding), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Verdict));
    }

    /// <summary>One object of the area stands; at a tenth of a percent, 1 in 1000 is just allowed and 1 in 999 is not.</summary>
    [Theory]
    [InlineData(1000, AreaVerdict.Cleared)]
    [InlineData(999, AreaVerdict.StillStanding)]
    public void OneStandingObjectInAThousandIsTheMostATenthOfAPercentAllows(int objects, AreaVerdict expected)
    {
        const int Side = 32;
        const float Spacing = 20f;
        ImmutableArray<TargetObject> targets = [.. Enumerable.Range(0, objects).Select(index => Own(index, new Vector3(index % Side * Spacing, index / Side * Spacing, 0)))];

        var judged = Assert.Single(Plan(targets, removed: [.. Enumerable.Range(1, objects - 1)], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((objects, 1, expected), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Standing, judged.JudgedPlace.Verdict));
        Assert.Equal(judged.JudgedPlace.CountPercent, judged.JudgedPlace.SizePercent, precision: 9);
    }

    /// <summary>A big object stands among three small removed ones: a quarter of them by count, nearly all by size.</summary>
    [Fact]
    public void OneBigObjectStandingAmongSmallRemovedOnesKeepsItsAreaBySize()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0)),
        ];

        var judged = Assert.Single(Plan(targets, removed: [1, 2, 3], [ChangedRock(0x900, Vector3.Zero)], settings: HalfMayStand).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 25d), (judged.JudgedPlace!.Verdict, judged.JudgedPlace.CountPercent));
        Assert.Equal((BigBlockVolume, BigBlockVolume + 3 * BlockVolume), (judged.JudgedPlace.StandingSize, judged.JudgedPlace.Size));
    }

    /// <summary>A big object was removed among three small ones that stand: nearly nothing by size, three quarters by count.</summary>
    [Fact]
    public void SmallObjectsStandingBesideOneBigRemovedOneKeepTheirAreaByCount()
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0)),
        ];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: HalfMayStand).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 75d), (judged.JudgedPlace!.Verdict, judged.JudgedPlace.CountPercent));
        Assert.True(judged.JudgedPlace.SizePercent < 50);
    }

    /// <summary>A big object was removed beside a small one that stands: under 1% by size but half by number, so the area is not cleared.</summary>
    [Fact]
    public void OneBigRemovedObjectBesideASmallStandingOneDoesNotClearTheArea()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(300, 0, 0), BigBlock), Own(1, new Vector3(0, 100, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: AtTheDefaults).Objects);

        Assert.Equal((AreaVerdict.StillStanding, 50d), (judged.JudgedPlace!.Verdict, judged.JudgedPlace.CountPercent));
        Assert.True(judged.JudgedPlace.SizePercent < 1);
    }

    /// <summary>Effect objects have no size, so their number alone decides: two of three still stand.</summary>
    [Fact]
    public void AnAreaOfOnlyEffectObjectsIsJudgedByNumberAlone()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), Fog), Own(1, new Vector3(-100, 0, 0), Fog), Own(2, new Vector3(0, 100, 0), Fog)];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)], settings: AtTheDefaults).Objects);

        Assert.Equal((0d, AreaVerdict.StillStanding), (judged.JudgedPlace!.Size, judged.JudgedPlace.Verdict));
    }

    /// <summary>Objects without a volume leave no size to weigh, so their count alone decides.</summary>
    [Fact]
    public void AnAreaWhoseObjectsHaveNoVolumeIsJudgedByCount()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(0, 100, 0), Flat), Own(1, new Vector3(0, -100, 0), Flat)];

        var judged = Assert.Single(Plan(targets, removed: [1], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((2, 0d, 50d, 0d, AreaVerdict.StillStanding), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Size, judged.JudgedPlace.CountPercent, judged.JudgedPlace.SizePercent, judged.JudgedPlace.Verdict));
    }

    /// <summary>
    /// A fog volume of the cleaned mod far larger than its other pieces was removed; three small pieces stand. The fog counts once and
    /// adds no size, so the standing pieces keep the area by count and by size.
    /// </summary>
    [Fact]
    public void ALargeRemovedEffectVolumeCountsOnceAndAddsNoSize()
    {
        ImmutableArray<TargetObject> targets = [Own(0, Vector3.Zero, Fog), Own(1, new Vector3(0, 100, 0)), Own(2, new Vector3(-100, 0, 0)), Own(3, new Vector3(0, -100, 0))];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((4, 3 * BlockVolume, 75d, 100d, AreaVerdict.StillStanding), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Size, judged.JudgedPlace.CountPercent, judged.JudgedPlace.SizePercent, judged.JudgedPlace.Verdict));
        Assert.False(judged.IsRestored);
    }

    /// <summary>The cleaned mod is made only of effect objects, so the area has no size and is judged by count alone.</summary>
    [Theory]
    [InlineData(new[] { 0, 1, 2 }, AreaVerdict.Cleared)]
    [InlineData(new[] { 0, 1 }, AreaVerdict.StillStanding)]
    public void AnAreaOfOnlyEffectObjectsIsJudgedByCount(int[] removed, AreaVerdict expected)
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0), Fog), Own(1, new Vector3(-100, 0, 0), Fog), Own(2, new Vector3(0, 100, 0), Fog)];

        var judged = Assert.Single(Plan(targets, removed, [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((3, 0d, expected), (judged.JudgedPlace!.Objects, judged.JudgedPlace.Size, judged.JudgedPlace.Verdict));
    }

    [Fact]
    public void AnInteriorIsOneAreaSoAnObjectFarFromTheOldPlaceCounts()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(20000, 0, 0))];
        var interior = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, null, SpaceKind.Interior, null) };

        var inInterior = Assert.Single(Plan(targets, removed: [], [ChangedRock(0x900, Vector3.Zero)], spaces: interior).Objects);
        var inWorldspace = Assert.Single(Plan(targets, removed: [], [ChangedRock(0x900, Vector3.Zero)]).Objects);

        Assert.Equal((AreaVerdict.StillStanding, AreaVerdict.NoObjectsAround), (inInterior.JudgedPlace!.Verdict, inWorldspace.JudgedPlace!.Verdict));
    }

    /// <summary>Another plugin's object the cleaned mod moved from a cleared place into a village that stays: it goes back, without a copy when it may not be copied.</summary>
    [Theory]
    [InlineData(false, AreaVerdict.StillStanding, true)]
    [InlineData(true, AreaVerdict.Cleared, false)]
    public void AnUndecidedMovedTargetFromAClearedPlaceGoesBackWithoutACopyWhenItMayNotBeCopied(bool villageRemoved, AreaVerdict whereItIsNow, bool copyRefused)
    {
        var village = new Vector3(5000, 0, 0);
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(100, 0, 0)),
            Own(1, village + new Vector3(100, 0, 0)),
            Own(2, village) with { Origin = TargetOrigin.Moved },
        ];

        var judged = Assert.Single(Plan(targets, removed: villageRemoved ? [0, 1] : [0], [ChangedRock(0x900, Vector3.Zero, target: new TargetId(2))]).Objects);

        Assert.Equal((AreaVerdict.Cleared, whereItIsNow, true), (judged.JudgedPlace!.Verdict, judged.WhereItIsNow!.Verdict, judged.IsRestored));
        Assert.Equal(copyRefused, judged.Cloning is { RefusedBy: not null });
    }

    /// <summary>The moved place has no object of the cleaned mod around it: only the old place decides.</summary>
    [Fact]
    public void AMovedObjectWhoseOldPlaceIsClearedGoesBackWhereverItStandsNow()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(5000, 0, 0)) with { Origin = TargetOrigin.Moved }];

        var judged = Assert.Single(Plan(targets, removed: [0], [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1))]).Objects);

        Assert.Equal((AreaVerdict.Cleared, AreaVerdict.NoObjectsAround, true), (judged.JudgedPlace!.Verdict, judged.WhereItIsNow!.Verdict, judged.IsRestored));
    }

    [Theory]
    [InlineData(new int[0], AreaVerdict.StillStanding)]
    [InlineData(new[] { 1 }, AreaVerdict.Cleared)]
    public void AMovedObjectWhoseOldPlaceIsNotClearedStaysWhateverIsAroundItsMovedPlace(int[] removed, AreaVerdict movedPlace)
    {
        ImmutableArray<TargetObject> targets =
        [
            Own(0, new Vector3(100, 0, 0)),
            Own(1, new Vector3(5100, 0, 0)),
            Own(2, new Vector3(5000, 0, 0)) with { Origin = TargetOrigin.Moved },
        ];

        var judged = Assert.Single(Plan(targets, removed, [ChangedRock(0x900, Vector3.Zero, target: new TargetId(2))]).Objects);

        Assert.Equal((AreaVerdict.StillStanding, movedPlace, false), (judged.JudgedPlace!.Verdict, judged.WhereItIsNow!.Verdict, judged.IsRestored));
        Assert.Equal(AreaVerdict.StillStanding, judged.KeptForArea);
    }

    [Fact]
    public void AMovedObjectWhoseOldPlaceStandsStaysWhenNoObjectIsAroundItsMovedPlace()
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(20000, 0, 0)) with { Origin = TargetOrigin.Moved }];

        var judged = Assert.Single(Plan(targets, removed: [], [ChangedRock(0x900, Vector3.Zero, target: new TargetId(1))]).Objects);

        Assert.Equal((AreaVerdict.StillStanding, AreaVerdict.NoObjectsAround, false), (judged.JudgedPlace!.Verdict, judged.WhereItIsNow!.Verdict, judged.IsRestored));
    }

    /// <summary>A later plugin holds the position, so only the turn goes back and the object ends where it stands now: that area decides.</summary>
    [Theory]
    [InlineData(new[] { 0 }, AreaVerdict.StillStanding, false)]
    [InlineData(new[] { 1 }, AreaVerdict.Cleared, true)]
    public void AnObjectWhosePositionALaterPluginHoldsIsJudgedWhereItStandsNow(int[] removed, AreaVerdict judgedArea, bool restored)
    {
        ImmutableArray<TargetObject> targets = [Own(0, new Vector3(100, 0, 0)), Own(1, new Vector3(5100, 0, 0))];
        var turned = ChangedRock(0x900, Vector3.Zero, undo: PlacedParts.Rotation) with { WhereItIsNow = new EulerPlacement(new Vector3(5000, 0, 0), Vector3.Zero, 1f) };

        var judged = Assert.Single(Plan(targets, removed, [turned]).Objects);

        Assert.Equal((judgedArea, restored), (judged.JudgedPlace!.Verdict, judged.IsRestored));
    }
}
