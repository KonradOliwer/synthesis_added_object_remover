using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using static AddedObjectRemover.Tests.Rules.RestoreWhereRemovedGuardScenes;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Rocks going back from under objects that stand on them, or that stood on a table going back, over in-memory scenes; and the
/// results of the guards whatever the number of workers or the order of what is read.
/// </summary>
public sealed class RestoreWhereRemovedGuardLanternTests
{
    /// <summary>
    /// A later mod's lantern stands on top of the rock where the cleaned mod put it, or a little above it. The lantern is no object of
    /// the cleaned mod, so the rock goes back from under it (decisions 31 and 32).
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void ARockGoesBackFromUnderALaterModsLantern(float gapBelowLantern)
    {
        var lantern = LaterObject(0, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize + gapBelowLantern));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A base game lantern no plugin changed stands on top of the rock where the cleaned mod put it; before the cleaned mod it stood
    /// there without the rock. The rock goes back to its earlier shape and place, so both end as they stood then.
    /// </summary>
    [Fact]
    public void ARockGoingBackToItsEarlierPlaceFromUnderAnObjectThatStoodThereBeforeTheCleanedModIsNotRefused()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize));
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A later mod that loads without the cleaned mod added a lantern on top of the rock where the cleaned mod put it, or moved a base
    /// game lantern there. The lantern is no object of the cleaned mod, so the rock goes back from under it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARockGoesBackFromUnderAnObjectALaterModAddedOrMovedOntoIt(bool added)
    {
        var onTop = PlaceNow + new Vector3(0, 0, RockHalfSize);
        var lantern = added
            ? TestShapes.Placed(LaterMod, 5, Lantern.Base, onTop)
            : TestShapes.Placed(BaseGameMod, 5, Lantern.Base, onTop) with { WinningMod = Later };
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>
    /// A later mod re-saved a base game lantern on top of the rock where the cleaned mod put it, with the same values: it stands as it
    /// stood before the cleaned mod, so the rock goes back from under it.
    /// </summary>
    [Fact]
    public void ARockGoingBackFromUnderAnObjectALaterModOnlyReSavedIsNotRefused()
    {
        var lantern = TestShapes.Placed(BaseGameMod, 5, Lantern.Base, PlaceNow + new Vector3(0, 0, RockHalfSize)) with { WinningMod = Later };
        var scene = new Scene { Rocks = [Rock(0, OldPlace, PlaceNow)], Others = [lantern, RockNow(0, PlaceNow)] };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    /// <summary>Rock B lies on rock A where the cleaned mod put them; both go back, so nothing is left floating.</summary>
    [Fact]
    public void ARockRestingOnAnotherThatGoesBackTooKeepsNeither()
    {
        var bNow = PlaceNow + new Vector3(0, 0, 2 * RockHalfSize);
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, new Vector3(-300, 0, 0), bNow)],
            Others = [RockNow(0, PlaceNow), RockNow(1, bNow)],
        };

        Assert.All(Plan(scene).Objects, judged => Assert.True(judged.IsRestored));
    }

    /// <summary>The top of a rock at its old place, where a lantern stood on it before the cleaned mod moved both away.</summary>
    private static readonly Vector3 OnTheOldPlace = OldPlace + new Vector3(0, 0, RockHalfSize);

    private static readonly Vector3 LanternNow = new(-300, 0, 0);

    /// <summary>
    /// The cleaned mod moved a base game table and the lantern on it away from their old places. A later mod's crate, low beside the
    /// table's old place, refuses the table, which stays where the cleaned mod put it; the lantern still goes back, as its earlier
    /// version. Without the crate both go back.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALanternGoesBackWhetherOrNotTheTableItStoodOnGoesBack(bool tableRefused)
    {
        var crate = LaterObject(2, Block.Base, OldPlace + new Vector3(ZoneReach - 5f, 0, -RockHalfSize));
        var scene = new Scene
        {
            Rocks = [Rock(0, OldPlace, PlaceNow), Rock(1, OnTheOldPlace, LanternNow, Lantern.Base)],
            Others = [RockNow(0, PlaceNow), RockNow(1, LanternNow, Lantern.Base), .. tableRefused ? new[] { crate } : []],
        };

        var refusals = Plan(scene).Objects.Select(judged => judged.Refused);

        Assert.Equal(
            tableRefused
                ? [new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, crate.Key, Failure: null), null]
                : new PlacedRefusal?[] { null, null },
            refusals);
    }

    /// <summary>The cleaned mod moved a lantern off a base game table; the patch removes the table, or keeps it. The lantern goes back either way.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALanternGoesBackWhetherOrNotThePatchRemovesTheTableItStoodOn(bool tableRemoved)
    {
        var table = TestShapes.Placed(BaseGameMod, 1, Block.Base, OldPlace) with { Id = new OtherId(0) };
        var removal = new OtherModsObjectOutcome(table, OtherModsObjectFate.Removed, LostShare: 1f, Drop: 0f, ReferenceRoles.None);
        var scene = new Scene
        {
            Rocks = [Rock(0, OnTheOldPlace, LanternNow, Lantern.Base)],
            Others = [table, RockNow(0, LanternNow, Lantern.Base)],
            OtherMods = tableRemoved ? new OtherModsObjectsResult(StepOutcome.Performed, [removal], [], [], Failure: null) : OtherModsObjectsResult.SwitchedOff,
        };

        var judged = Assert.Single(Plan(scene).Objects);

        Assert.Equal((true, null), (judged.IsRestored, judged.Refused));
    }

    [Fact]
    public void TheGuardsGiveTheSameResultsWhateverTheNumberOfWorkers()
    {
        var one = Plan(ManyRocks(workers: 1));
        var eight = Plan(ManyRocks(workers: 8));

        Assert.Equal(one.Objects.Select(Describe), eight.Objects.Select(Describe));
        Assert.Equal(one.FollowUp.Others.Select(other => other.Outcome), eight.FollowUp.Others.Select(other => other.Outcome));
        Assert.Equal(one.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)), eight.FollowUp.Others.Select(other => string.Join(", ", other.StartedBy)));
        Assert.Contains(one.Objects, judged => judged.IsRestored);
        Assert.Equal(
            [PlacedGuard.TooCloseToWhatStays, PlacedGuard.StandsInChangedNavmesh],
            one.Objects.Select(judged => judged.Refused?.Guard).OfType<PlacedGuard>().Distinct().Order());
    }

    [Fact]
    public void TheGuardsGiveTheSameResultsWhateverTheOrderOfTheRecordsAndObjectsRead()
    {
        var random = new Random(5);
        var scene = ManyRocks(workers: 1);
        var shuffled = scene with { Rocks = [.. scene.Rocks.OrderBy(_ => random.Next())], Others = [.. scene.Others.OrderBy(_ => random.Next())] };

        Assert.Equal(Plan(scene).Objects.Select(Describe), Plan(shuffled).Objects.Select(Describe));
    }

    /// <summary>
    /// Forty rocks in a row, each moved by the cleaned mod to a second row; some go back next to a later mod's crate, around a
    /// marker of the cleaned mod, into a navmesh the cleaned mod added, or carry a later mod's lantern or a base game lantern no plugin
    /// changed where they stand, and some were moved onto the next rock's old place.
    /// </summary>
    private static Scene ManyRocks(int workers)
    {
        const int Count = 40;
        const float Spacing = 100f;
        var targets = Enumerable.Range(0, 9).Select(index => Own(index, new Vector3(index * 500f, 300, 0))).ToList();
        var rocks = new List<RecordChangedByCleanedMod>();
        var others = new List<OtherObject>();
        for (var index = 0; index < Count; index++)
        {
            var oldPlace = new Vector3(index * Spacing, 0, 0);
            var now = index % 7 == 0 ? oldPlace + new Vector3(Spacing + 5, 0, 0) : new Vector3(index * Spacing, 2000, 0);
            rocks.Add(Rock(index, oldPlace, now));
            others.Add(RockNow(index, now));
            if (index % 4 == 1) others.Add(LaterObject(index, Block.Base, oldPlace + new Vector3(0, ZoneReach - 5f, 0)));
            if (index % 5 == 2) targets.Add(TestTargets.Create(targets.Count, TestTargets.At(oldPlace + new Vector3(5, 0, 0)), Marker, TestTargets.Space));
            if (index % 6 == 3) others.Add(LaterObject(Count + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize)));
            if (index % 8 == 5) others.Add(TestShapes.Placed(BaseGameMod, 2 * Count + index, Lantern.Base, now + new Vector3(0, 0, RockHalfSize)));
        }
        return new Scene
        {
            Targets = targets,
            Removed = [.. Enumerable.Range(0, 9)],
            Rocks = rocks,
            Others = others,
            Navmesh = [new NavmeshWorldTriangle(ChangedNavmeshKey, FlatTriangle(new Vector3(1000, 0, 0)))],
            ChangedNavmeshes = [ChangedNavmeshKey],
            Workers = workers,
        };
    }

    private static string Describe(PlacedChangeJudged judged) => $"{judged.Change.Key} {judged.Refused} {judged.IsRestored}";
}
