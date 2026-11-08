using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Whole runs where a moved replacement or an object of the cleaned mod is shrunk or turned to fit, or removed instead.</summary>
public class MovedReplacementFittingTests
{
    private static readonly Vector3 BesideTheBottom = Original + new Vector3(75, 0, 0);

    /// <summary>Only the size margin, so the crate shrunk to 90% keeps clear of the wall that the full crate's margin reaches.</summary>
    [Fact]
    public void AMovedReplacementNextToAWallIsShrunkWhereTheCleanedModPutIt()
    {
        IPlacedObjectGetter? written = null;
        var outcome = RunCrateWorld(RemoveAfterResizing(ShrinkSettings()), movedTo: MovedTo, walls: [NearMovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Equal(MovedTo, PositionOf(written!));
        Assert.Equal(0.9f, written!.Scale ?? 1f, 5);
        Assert.False(IsInitiallyDisabled(written!));
    }

    [Fact]
    public void AMovedReplacementThatFitsNowhereIsRemovedLikeTheCleanedModsObjects()
    {
        IPlacedObjectGetter? written = null;
        var outcome = RunCrateWorld(RemoveAfterResizing(ShrinkSettings()), movedTo: MovedTo, walls: [NearMovedTo, MovedTo], inspectPatch: patch => written = FindBaseGameObject(patch));

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
        Assert.True(IsInitiallyDisabled(written!));
    }

    [Fact]
    public void AMovedReplacementNextToAWallIsRemovedWhenShrinkingAndTurningAreSwitchedOff()
    {
        var outcome = RunCrateWorld(RemoveAfterResizing(ShrinkSettings(shrinkLimitPercent: 100, turnLimitDegrees: 0)), movedTo: MovedTo, walls: [NearMovedTo]);

        Assert.Equal(ReplacementOf(outcome).Id, Assert.Single(outcome.Final.RemovedTargets()));
    }

    /// <summary>
    /// The cleaned mod's crate on top of its other crate fits shrunk beside a city wall, but the crate it rests on is removed for a
    /// wall standing in it: also-remove takes the fitted crate along like any object still standing.
    /// </summary>
    [Fact]
    public void AnObjectKeptShrunkIsRemovedByAlsoRemoveWhenWhatItRestsOnIsRemoved()
    {
        IPlacedObjectGetter? writtenTop = null;
        var outcome = RunStackWorld(SupportSettings(), [BesideTheTop, Original], patch => writtenTop = FindTarget(patch, "T_Top"));

        var (top, bottom) = StackOf(outcome);
        Assert.Equal(bottom.Id, Assert.IsType<LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason).MainSupporter);
        Assert.True(IsInitiallyDisabled(writtenTop!));
        Assert.Equal(1f, writtenTop!.Scale ?? 1f);
    }

    /// <summary>The bottom crate only touches the top crate with its top face, far from its base, so it does not rest on the top crate.</summary>
    [Fact]
    public void AnObjectKeptShrunkStaysWhenWhatItRestsOnStays()
    {
        var outcome = RunStackWorld(SupportSettings(), [BesideTheTop]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<Decision.Fitted>(outcome.Final.Of(top.Id));
        Assert.Null(outcome.Final.Of(bottom.Id));
    }

    /// <summary>The bottom crate would fit shrunk beside a city wall, but the top crate rests on it.</summary>
    [Fact]
    public void AnObjectOthersRestOnIsRemovedInsteadOfShrunkAndWhatRestsOnItGoesWithIt()
    {
        var outcome = RunStackWorld(SupportSettings(), [BesideTheBottom]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(bottom.Id)).Reason);
        Assert.Equal(bottom.Id, Assert.IsType<LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason).MainSupporter);
    }

    /// <summary>
    /// With EverythingTouchingIt too, the bottom crate only touches the top crate from below and does not rest on it, so the top
    /// crate is shrunk to fit and the bottom crate stays.
    /// </summary>
    [Fact]
    public void WithEverythingTouchingItAnObjectOnlyTouchedFromBelowIsShrunk()
    {
        var outcome = RunStackWorld(ShrinkSettings(), [BesideTheTop]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<Decision.Fitted>(outcome.Final.Of(top.Id));
        Assert.Null(outcome.Final.Of(bottom.Id));
    }

    /// <summary>With EverythingTouchingIt, as with OnlyWhatLosesItsSupport, the top crate resting on the bottom crate keeps it from being shrunk.</summary>
    [Fact]
    public void WithEverythingTouchingItAnObjectOthersRestOnIsRemovedInsteadOfShrunkAndWhatTouchesItGoesWithIt()
    {
        var outcome = RunStackWorld(ShrinkSettings(), [BesideTheBottom]);

        var (top, bottom) = StackOf(outcome);
        Assert.IsType<TooClose>(Assert.IsType<Decision.Removed>(outcome.Final.Of(bottom.Id)).Reason);
        Assert.Equal(new TouchingTargetThatLeft(bottom.Id), Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason);
    }

    [Fact]
    public void TheCleanedModsOwnObjectIsRemovedWhenShrinkingAndTurningAreSwitchedOff()
    {
        var outcome = RunStackWorld(SupportSettings(shrinkLimitPercent: 100, turnLimitDegrees: 0), [BesideTheTop]);

        Assert.IsType<Decision.Removed>(outcome.Final.Of(StackOf(outcome).Top.Id));
    }

    /// <summary>
    /// The cleaned mod's twin pillars with a city pad 5 units east of the east pillar: turned 15 degrees either way the pillar is
    /// still within the 32-unit margin, turned 30 degrees it is clear.
    /// </summary>
    [Fact]
    public void AnObjectThatCannotBeShrunkIsTurnedByTheSmallestTurnThatFitsAndThePatchHoldsTheTurn()
    {
        IPlacedObjectGetter? written = null;
        var settings = ShrinkSettings(shrinkLimitPercent: 100, turnLimitDegrees: 90);
        settings.Standard.TooClose = new TooCloseRemovalSettings
        {
            BySize = new TooCloseBySizeSettings { Use = false },
            ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
        };
        var outcome = Assert.IsType<RunResult.Done>(FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreatePillarWorld, inspectPatch: patch => written = FindTarget(patch, "T_Pillars"))).Outcome;

        var pillars = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Pillars");
        var fitted = Assert.IsType<Decision.Fitted>(outcome.Final.Of(pillars.Id));
        Assert.Equal(float.DegreesToRadians(30), fitted.To.Rotation.Z, 4);
        Assert.Equal(1f, fitted.To.Scale);
        Assert.Equal(float.DegreesToRadians(30), written!.Placement!.Rotation.Z, 4);
        Assert.False(IsInitiallyDisabled(written));
    }

    /// <summary>The base game's object that the cleaned mod overrides, as the patch holds it; null when the patch does not hold it.</summary>
    private static IPlacedObjectGetter? FindBaseGameObject(ISkyrimModGetter patch) =>
        patch.EnumerateMajorRecords<IPlacedObjectGetter>().SingleOrDefault(placed => placed.FormKey.ModKey == ModKey.FromNameAndExtension(BaseGamePlugin));

    private static Vector3 PositionOf(IPlacedObjectGetter placed) => new(placed.Placement!.Position.X, placed.Placement.Position.Y, placed.Placement.Position.Z);

    private static bool IsInitiallyDisabled(IPlacedObjectGetter placed) =>
        (placed.MajorRecordFlagsRaw & (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled) != 0;

    /// <param name="walls">Where the city mod's walls stand.</param>
    private static RunOutcome RunStackWorld(Settings settings, IReadOnlyList<Vector3> walls, Action<ISkyrimModGetter>? inspectPatch = null)
    {
        var result = FixtureRun.Run(
            settings, Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: dataFolder => CreateStackWorld(dataFolder, walls), inspectPatch: inspectPatch);
        return Assert.IsType<RunResult.Done>(result).Outcome;
    }

    private static (TargetObject Top, TargetObject Bottom) StackOf(RunOutcome outcome) =>
        (Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Top"),
            Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_Bottom"));

    private static IPlacedObjectGetter FindTarget(ISkyrimModGetter patch, string editorId) =>
        patch.EnumerateMajorRecords<IPlacedObjectGetter>().Single(placed => placed.EditorID == editorId);

    /// <summary>The cleaned mod's twin pillars in the interior and a city pad 5 units east of the east pillar.</summary>
    private static IReadOnlyList<ISkyrimMod> CreatePillarWorld(string dataFolder)
    {
        var world = CreateWorld(dataFolder);
        world.Target.Interior.Place("T_Pillars", world.Target.AddModeledStatic("T_PillarsBase", FixtureMeshes.TwinPillarsModel), Original);
        PlaceWalls(world.City, world.City.Interior, [Original + new Vector3(175, 0, 0)]);
        return world.LoadOrder;
    }
}
