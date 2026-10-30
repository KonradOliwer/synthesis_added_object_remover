using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using static AddedObjectRemover.Tests.EndToEnd.RingWorld;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The visible left-behind rounds through a whole run of the ring world with one more crate of the cleaned mod, the outer crate, standing
/// east of the ring. All its other objects lie to its west, so it has one direction with objects, and that direction counts as cleared
/// only when every object in it is gone. The six ring crates are removed for being too close, which leaves the middle crate cleared
/// in the first visible round but not the outer crate, which still sees the middle crate standing. Once the middle crate is gone the outer
/// crate is cleared, in the next round: after the middle crate's removal alone, or, with the door resting on the middle crate, after the
/// follow-up has removed the door as well.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public class VisibleLeftBehindRoundsRunTests
{
    private const string OuterCrate = "T_OuterCrate";
    private const float OuterCrateX = 600f;
    private const int VisibleRounds = 2;

    public static TheoryData<FollowUpRemovalMode> TouchingModes => new(Enum.GetValues<FollowUpRemovalMode>().Where(mode => mode != FollowUpRemovalMode.Nothing));

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void AnObjectWhoseSurroundingsAreStillPartlyStandingInTheFirstRoundIsRemovedInTheSecondOnceTheMiddleCrateIsGone(int workers)
    {
        var log = new RecordingLogSink();

        var outcome = ((RunResult.Done)FixtureRun.Run(
            CreateSettings(FollowUpRemovalMode.Nothing, FullyClearedDirectionsOnly()), workers, log, DefaultMeshFiles, createLoadOrder: dataFolder => RingWorld.Create(dataFolder, AddOuterCrate))).Outcome;

        var middle = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, MiddleCrate)));
        var outer = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, OuterCrate)));
        Assert.IsType<RemovalReason.VisibleObjectSurroundingsRemoved>(middle.Reason);
        Assert.IsType<RemovalReason.VisibleObjectSurroundingsRemoved>(outer.Reason);
        Assert.Equal(new[] { middle.Round, outer.Round }, outcome.VisibleLeftBehind.Rounds.ToArray());
        Assert.Equal(RingCrates + 2, outcome.Removals.Length);
        Assert.Contains(
            log.Sections.Single(section => section.Id == "visible-leftovers").Lines,
            line => line.StartsWith($"Visible objects left behind: {VisibleRounds} rounds in ", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TouchingModes))]
    public void AnObjectRemovedByTheFollowUpOfTheFirstRoundClearsTheSurroundingsOfAnotherObjectThatTheSecondRoundRemoves(FollowUpRemovalMode mode)
    {
        var outcome = FixtureRun.Execute(CreateSettings(mode, FullyClearedDirectionsOnly()), workers: 1, dataFolder => RingWorld.Create(dataFolder, AddDoorAndOuterCrate));

        var middle = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, MiddleCrate)));
        var door = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, DoorEditorId)));
        var outer = Assert.IsType<Decision.Removed>(outcome.Final.Of(IdOf(outcome, OuterCrate)));
        Assert.Equal(RoundKind.VisibleLeftBehind, middle.Round.Kind);
        Assert.Equal(RoundKind.AlsoRemove, door.Round.Kind);
        Assert.Equal(RoundKind.VisibleLeftBehind, outer.Round.Kind);
        Assert.True(middle.Round.Number < door.Round.Number);
        Assert.True(door.Round.Number < outer.Round.Number);
        Assert.IsType<RemovalReason.VisibleObjectSurroundingsRemoved>(outer.Reason);
        Assert.Equal(new[] { middle.Round, outer.Round }, outcome.VisibleLeftBehind.Rounds.ToArray());
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    /// <summary>A direction is cleared only when all of it is gone; a share of 70% of the directions with objects suffices, also with one of them standing.</summary>
    private static VisibleSurroundingObjectsSettings FullyClearedDirectionsOnly() =>
        new() { MinOccupiedDirections = 1, DirectionThresholdPercent = 100, ClearedDirectionsPercent = 70 };

    private static void AddOuterCrate(RingWorldMods mods) =>
        mods.Target.Interior.Place(OuterCrate, mods.Target.AddModeledStatic("T_OuterCrateBase", FixtureMeshes.CrateModel), new Vector3(OuterCrateX, 0f, 0f));

    private static void AddDoorAndOuterCrate(RingWorldMods mods)
    {
        AddDoorWithPadOnIt(mods);
        AddOuterCrate(mods);
    }
}
