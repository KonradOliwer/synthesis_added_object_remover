using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Whole runs of base-game objects that the cleaned mod moved or replaced, in every restore mode.</summary>
public class MovedReplacementModesTests
{
    [Fact]
    public void AReplacementLeftWhereItStoodIsNotCheckedEvenNextToAWall()
    {
        var outcome = RunCrateWorld(movedTo: Original, walls: [Original + new Vector3(75, 0, 0)]);

        Assert.Single(outcome.Collected.Targets);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Theory]
    [InlineData(RestoreMode.Restore)]
    [InlineData(RestoreMode.RestoreIfNoCollision)]
    [InlineData(RestoreMode.Remove)]
    public void AMovedReplacementThatFitsWhereTheCleanedModPutItStaysThereInEveryMode(RestoreMode mode)
    {
        var outcome = RunCrateWorld(CrateSettings().WithMovedObjects(mode), movedTo: MovedTo, walls: []);

        Assert.Null(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    [Fact]
    public void ByDefaultAMovedReplacementThatWouldBeRemovedIsRestoredWhereItStoodWhenThatPlaceIsFree()
    {
        var outcome = RunCrateWorld(movedTo: MovedTo, walls: [NearMovedTo]);

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(Original, restored.EarlierVersion.Placement.Position);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Fact]
    public void ByDefaultAMovedReplacementThatWouldBeRemovedIsRemovedWhenThePlaceItStoodCollides()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateCrateWorldWithAnOwnCrateAtTheOldPlace);

        Assert.IsType<TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id)).Reason);
        Assert.Empty(outcome.Restorations);
    }

    [Fact]
    public void ByDefaultAMovedReplacementIsNeverShrunkOrTurnedEvenWhereShrinkingWouldFit()
    {
        var outcome = RunCrateWorld(ShrinkSettings(), movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    [Fact]
    public void WithResizingOnAMovedReplacementThatFitsShrunkStaysWhereTheCleanedModPutItAndIsNeitherRestoredNorRemoved()
    {
        var settings = ShrinkSettings().WithMovedObjects(RestoreMode.RestoreIfNoCollision, attemptResizing: true);

        var outcome = RunCrateWorld(settings, movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Single(outcome.ChangedObjects.OldPlaceChecks);
        Assert.Empty(outcome.Restorations);
        Assert.Empty(outcome.Final.RemovedTargets());
    }

    [Fact]
    public void WithResizingOnAMovedReplacementThatFitsNowhereIsLeftToTheMode()
    {
        var settings = ShrinkSettings().WithMovedObjects(RestoreMode.RestoreIfNoCollision, attemptResizing: true);

        var outcome = RunCrateWorld(settings, movedTo: MovedTo, walls: [NearMovedTo, MovedTo]);

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
    }

    /// <summary>The cleaned mod gave the base game's crate the model of a building without moving it, and a city wall stands beside the building's edge.</summary>
    [Fact]
    public void AReplacedObjectIsIgnoredWhenReplacedObjectsAreNotProcessed()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateReplacedWorld);

        Assert.DoesNotContain(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Empty(outcome.Restorations);
    }

    [Fact]
    public void AProcessedReplacedObjectIsJudgedLikeAMovedOneAndRestoredByDefaultWhenItsOldPlaceIsFree()
    {
        var outcome = FixtureRun.Execute(CrateSettings().WithReplacedObjects(process: true, SettingDefaults.ReplacedObjectsWhenRemoved), Workers, CreateReplacedWorld);

        var replaced = Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.IsType<Decision.Restored>(outcome.Final.Of(replaced.Id));
    }

    [Fact]
    public void AProcessedReplacedObjectIsRemovedWhenItsModeIsRemove()
    {
        var outcome = FixtureRun.Execute(CrateSettings().WithReplacedObjects(process: true, RestoreMode.Remove), Workers, CreateReplacedWorld);

        var replaced = Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        Assert.IsType<Decision.Removed>(outcome.Final.Of(replaced.Id));
    }

    /// <summary>The cleaned mod moved a base-game sound marker into a city mod's building: an invisible object has no shape to fit, so it is removed.</summary>
    [Fact]
    public void AnInvisibleReplacementMovedIntoAnotherModsObjectIsRemovedAsLeftInsideIt()
    {
        var outcome = FixtureRun.Execute(CrateSettings().WithMovedObjects(RestoreMode.Remove), Workers, CreateSoundWorld);

        var removal = Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.IsType<InsideAnotherModsObject>(removal.Reason);
    }

    /// <summary>A base-game sound marker in the interior that the cleaned mod moves into a city building.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateSoundWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        var sound = world.BaseGame.Interior.Place("B_Sound", world.BaseGame.AddSoundMarker("B_SoundBase"), Original + new Vector3(0, 0, 50));
        MoveCopy(world.Target.Interior.PlaceCopyOf(sound), MovedTo + new Vector3(0, 0, 50));
        var building = world.City.AddModeledStatic("C_Building", FixtureMeshes.BuildingModel);
        world.City.Interior.Place("C_BuildingAtTheNewSpot", building, MovedTo);
        return world.LoadOrder;
    }
}
