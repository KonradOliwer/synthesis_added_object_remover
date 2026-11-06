using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs over the campfire ring (<see cref="RestoreWhereRemovedWorlds.CreateCampfireRing"/>): a base game rock the cleaned mod
/// moved into the ring of its campfire goes back to its old place, and the patch adds a copy where the ring holds it.
/// </summary>
public sealed class CloneRestoreRunTests
{
    private const int InitiallyDisabled = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;

    /// <summary>The too-close distance of a stricter later step, which finds the campfire and the copy too close to the city pad.</summary>
    private const float StricterTooCloseDistance = 1000f;

    [Fact]
    public void ARockMovedIntoTheRingOfAStandingCampfireGoesBackAndThePatchAddsACopyWhereTheRingHoldsIt()
    {
        ISkyrimModGetter? patch = null;

        var outcome = FixtureRun.Execute(RestoreWhereRemovedRunTests.TerrainSettings(), workers: 1, RestoreWhereRemovedWorlds.CreateCampfireRing, inspectPatch: written => patch = written);

        var rock = Assert.Single(outcome.RestoreWhereRemoved.Objects);
        Assert.Equal((true, new CloneJudged(RefusedBy: null)), (rock.IsRestored, rock.Cloning));
        var copy = Assert.Single(outcome.Written.Clones);
        Assert.Equal(rock.Change.Key, copy.Original);
        var records = patch!.EnumerateMajorRecords<IPlacedObjectGetter>().ToDictionary(record => record.FormKey.ToRecordKey());
        Assert.Equal(RestoreWhereRemovedWorlds.RingRockFrom, PositionOf(records[copy.Original]));
        Assert.Equal(RestoreWhereRemovedWorlds.RingRockTo, PositionOf(records[copy.Copy]));
        Assert.Equal(CloneMarker.Of(copy.Original, new PluginName(FixtureWorld.TargetName)), records[copy.Copy].EditorID);
    }

    /// <summary>
    /// In a cleared camp whose campfire another plugin keeps, the left-behind step measures the rock the cleaned mod moved beside the
    /// campfire around it: the rock goes back with a copy left at the campfire; a persistent rock may not be copied and goes back without one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARockBesideTheKeptCampfireOfAClearedCampGoesBackWithACopyUnlessItMayNotBeCopied(bool persistentRock)
    {
        var outcome = FixtureRun.Execute(RestoredWorldStaysCleanTests.WithTheDirectionMethod(), workers: 1, dataFolder => RestoreWhereRemovedWorlds.CreateClearedCampWithAKeptCampfire(dataFolder, persistentRock));

        var rock = Assert.Single(outcome.RestoreWhereRemoved.Objects);
        Assert.Equal(
            (true, new CloneJudged(persistentRock ? CloneRefusal.Persistent : null)),
            (rock.IsRestored, rock.Cloning));
        Assert.Equal(persistentRock ? 0 : 1, outcome.Written.Clones.Length);
    }

    /// <summary>A rock with distant LOD, as a bridge or another large landmark has, may not be copied: it goes back and the patch adds no copy.</summary>
    [Fact]
    public void ALandmarkWithDistantLodMovedIntoTheRingIsNotCopiedAndGoesBack()
    {
        var outcome = FixtureRun.Execute(RestoreWhereRemovedRunTests.TerrainSettings(), workers: 1, RestoreWhereRemovedWorlds.CreateCampfireRingWithALandmark);

        var rock = Assert.Single(outcome.RestoreWhereRemoved.Objects);
        Assert.Equal(
            (true, (AreaVerdict?)null, new CloneJudged(CloneRefusal.HasDistantLod)),
            (rock.IsRestored, rock.KeptForArea, rock.Cloning));
        Assert.Empty(outcome.Written.Clones);
    }

    /// <summary>
    /// A later step for the same cleaned mod, with a too-close distance that reaches from the city pad to the campfire but not to the
    /// copy east of it, removes the campfire; the copy, judged as the cleaned mod's content, is then left behind with its surroundings
    /// removed and goes with it, disabled in the patch. The rock stays at its old place.
    /// </summary>
    [Fact]
    public void AStricterLaterStepForTheSameCleanedModRemovesTheCampfireAndTheCopyGoesWithItAsLeftBehind()
    {
        var stricter = RestoreWhereRemovedRunTests.TerrainSettings();
        stricter.Standard.TooClose.ByDistance.DistanceInUnits = StricterTooCloseDistance;
        stricter.Standard.LeftoverObjects = new LeftoverObjectSettings
        {
            MinObjectsAround = 1,
            RemoveBySurroundingDirections = true,
            SurroundingObjects = new VisibleSurroundingObjectsSettings { SearchRadius = LeftBehindSearchRadius, MinOccupiedDirections = 1, DirectionThresholdPercent = 10, ClearedDirectionsPercent = 10 },
        };
        ISkyrimModGetter? secondPatch = null;

        var (first, second) = RunTwice(RestoreWhereRemovedRunTests.TerrainSettings(), stricter, patch => secondPatch = patch);

        var copy = Assert.Single(first.Written.Clones);
        var reasons = second.Written.Removed.ToDictionary(id => second.Collected[id].EditorId ?? second.Collected[id].Key.ToString(), id => second.Final.Of(id) is Decision.Removed removed ? removed.Reason : null);
        Assert.IsType<TooClose>(reasons["T_Campfire"]);
        var copyReason = reasons[CloneMarker.Of(copy.Original, new PluginName(FixtureWorld.TargetName))];
        Assert.True(copyReason is VisibleObjectSurroundingsRemoved or InClearedArea, $"The copy was removed for {copyReason}.");
        var records = secondPatch!.EnumerateMajorRecords<IPlacedObjectGetter>().ToDictionary(record => record.FormKey.ToRecordKey());
        Assert.NotEqual(0, records[copy.Copy].MajorRecordFlagsRaw & InitiallyDisabled);
        Assert.Equal(RestoreWhereRemovedWorlds.RingRockFrom, PositionOf(records[copy.Original]));
    }

    /// <summary>The direction method's search radius in the stricter step: it reaches from the copy to the campfire and the crate.</summary>
    private const float LeftBehindSearchRadius = 1500f;

    /// <summary>
    /// A later run for another cleaned mod, on a load order that adds that mod, whose tent stands beside the copy: the copy only holds or
    /// supports objects there, so nothing of that mod is removed.
    /// </summary>
    [Fact]
    public void ALaterStepForAnotherCleanedModReadsTheCopyAsAnotherCleanedModsAndRemovesNothingBesideIt()
    {
        var otherCamp = RestoreWhereRemovedRunTests.TerrainSettings();
        otherCamp.Standard.CleanedMod = ModKey.FromNameAndExtension(RestoreWhereRemovedWorlds.OtherCampPlugin);

        var copies = RestoredWorldStaysClean.CheckWithAnotherCleanedMod(
            RestoreWhereRemovedRunTests.TerrainSettings(), RestoreWhereRemovedWorlds.CreateCampfireRing, otherCamp, RestoreWhereRemovedWorlds.CreateCampfireRingWithAnotherCamp);

        Assert.Equal(1, copies);
    }

    /// <summary>Runs the campfire ring, then again with the second settings and the first run's patch imported as the patch it adds to.</summary>
    private static (RunOutcome First, RunOutcome Second) RunTwice(Settings settings, Settings secondSettings, Action<ISkyrimModGetter> inspectSecondPatch)
    {
        var folder = Directory.CreateTempSubdirectory("aor-stricter-step-");
        try
        {
            var firstPatchFile = Path.Combine(folder.FullName, FixturePatcherState.PatchModKey.FileName);
            var first = FixtureRun.Execute(settings, workers: 1, RestoreWhereRemovedWorlds.CreateCampfireRing, inspectPatch: patch => patch.WriteToBinary(firstPatchFile));
            var second = FixtureRun.Execute(secondSettings, workers: 1, RestoreWhereRemovedWorlds.CreateCampfireRing, inspectSecondPatch, earlierPatchFile: firstPatchFile);
            return (first, second);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static Vector3 PositionOf(IPlacedObjectGetter placed) => new(placed.Placement!.Position.X, placed.Placement.Position.Y, placed.Placement.Position.Z);
}
