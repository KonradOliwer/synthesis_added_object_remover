using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Whole runs where a moved base-game object is restored to where it stood, and what an earlier patcher of the group changed again.</summary>
[ExpectedOutputTest]
public class MovedReplacementRunTests
{
    private const float CleanedModRadius = 50f;
    private const float CleanedModScale = 1.5f;
    private const float PatchScale = 2f;
    private static readonly PluginName PatchName = FixturePatcherState.PatchModKey.ToPluginName();

    [Fact]
    public void ARestoredMovedReplacementIsPutBackWhereItStoodAndNeverCalledRemovedInTheOutput()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Restore, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(Original, restored.EarlierVersion.Placement.Position);
        Assert.Empty(outcome.Final.RemovedTargets());
        Assert.Single(outcome.Restorations);
        var written = Assert.Single(run.Patch, line => line.Contains(BaseGamePlugin, StringComparison.Ordinal));
        Assert.Contains("disabled False", written);
        Assert.DoesNotContain(run.Log, line => line.StartsWith("  Removed ", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("Removed 0 objects (", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("Restored 1 objects of other mods that Target.esp changed (1 too close, ", StringComparison.Ordinal));
        ExpectedOutputFiles.AssertMatches("restored-objects/Restore", run.ToExpectedOutputLines());
    }

    [Fact]
    public void AMovedReplacementWhoseOldPlaceIsFreeIsRestoredAndTheCheckIsCounted()
    {
        var (outcome, run) = RunRestoring(RestoreMode.RestoreIfNoCollision, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Contains(run.Log, line => line.StartsWith("Checked the old places of 1 objects of other mods that Target.esp changed: 1 free, 0 colliding", StringComparison.Ordinal));
        Assert.Contains("free", Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredFileName).Lines[1]);
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoreIfNoCollisionFree", run.ToExpectedOutputLines());
    }

    /// <summary>The cleaned mod's own crate stands beside the old place, so the crate would collide there and is disabled where the cleaned mod put it.</summary>
    [Fact]
    public void AMovedReplacementWhoseOldPlaceCollidesWithTheCleanedModsObjectIsRemovedAndTheCollisionIsNamed()
    {
        var (outcome, run) = RunRestoring(RestoreMode.RestoreIfNoCollision, CreateCrateWorldWithAnOwnCrateAtTheOldPlace);

        Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Empty(outcome.Restorations);
        Assert.Contains(run.Log, line => line.StartsWith("Checked the old places of 1 objects of other mods that Target.esp changed: 0 free, 1 colliding", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("  Collides at its old place: B_Crate", StringComparison.Ordinal) && line.Contains("changed by Target.esp (new object)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Reports, report => report.Name == ReportFileNames.RestoredFileName);
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoreIfNoCollisionColliding", run.ToExpectedOutputLines());
    }

    /// <summary>A crate of the cleaned mod stands on the moved crate; the crate goes back to where it stood, so the one on it is left without support and removed.</summary>
    [Fact]
    public void AnObjectRestingOnARestoredMovedReplacementLosesItsSupportAndTheReasonCallsTheCrateRestored()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Restore, CreateCrateWorldWithAnOwnCrateOnTheMovedOne);

        var top = Assert.Single(outcome.Collected.Targets, target => target.EditorId == "T_OnTheMovedCrate");
        Assert.IsType<LostSupport>(Assert.IsType<Decision.Removed>(outcome.Final.Of(top.Id)).Reason);
        Assert.Contains(run.Log, line => line.Contains("of its support left its place (mostly B_Crate [000805:Skyrim.esm], restored)", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.Contains("support was removed (mostly B_Crate", StringComparison.Ordinal));
        ExpectedOutputFiles.AssertMatches("restored-objects/RestoredSupportLost", run.ToExpectedOutputLines());
    }

    [Fact]
    public void WithRemoveModeNothingIsCheckedAtTheOldPlaceAndNothingIsRestored()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Remove, dataFolder => CreateCrateWorld(dataFolder, MovedTo, [NearMovedTo]));

        Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.DoesNotContain(run.Log, line => line.StartsWith("Checked the old places", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.Contains("estored", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RestoreMode.Restore)]
    [InlineData(RestoreMode.RestoreIfNoCollision)]
    public void OneAndManyWorkersGiveTheSameOutputForARestore(RestoreMode mode)
    {
        Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder = CreateCrateWorldWithAnOwnCrateOnTheMovedOne;

        var one = RunRestoring(mode, createLoadOrder, workers: 1).Run.ToExpectedOutputLines();
        var many = RunRestoring(mode, createLoadOrder, workers: Workers).Run.ToExpectedOutputLines();

        Assert.Equal(one, many);
    }

    /// <summary>
    /// The cleaned mod sizes the crate to 1.5 as it moves it; an earlier patcher of the group carries the move and sizes it to 2. The
    /// position goes back and the size stays the patch's, and the report names the size as left to the patch and shows it.
    /// </summary>
    [Fact]
    public void ARestoreWhoseSizeAnEarlierPatcherChangedAgainReportsThatSizeAsTheNewOneAndNamesThePatch()
    {
        var crate = default(FormKey);
        var (outcome, run) = RunRestoring(
            RestoreMode.Restore,
            dataFolder => CreateCrateWorldMovedAndSized(dataFolder, found => crate = found),
            state => OverrideInPatch(state, crate).Scale = PatchScale);

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal([KeyValuePair.Create(PlacedParts.Scale, PatchName)], restored.EarlierVersion.LeftToLaterPlugins);
        var row = Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredFileName).Lines[1];
        Assert.EndsWith($",{BaseGamePlugin},0,0,0,0,2,,not checked,scale ({PatchName})", row);
    }

    /// <summary>An earlier patcher of the group moved the crate again after the cleaned mod, so its position does not go back and it is removed instead.</summary>
    [Fact]
    public void AnObjectWhosePositionAnEarlierPatcherChangedAgainIsRemovedAndTheReasonNamesThePatch()
    {
        var crate = default(FormKey);
        var (outcome, run) = RunRestoring(
            RestoreMode.Restore,
            dataFolder => CreateCrateWorldMovedAndSized(dataFolder, found => crate = found),
            state => OverrideInPatch(state, crate).Placement!.Position = new P3Float(MovedTo.X, MovedTo.Y + 5, MovedTo.Z));

        Assert.IsType<Decision.Removed>(outcome.Final.Of(ReplacementOf(outcome).Id));
        var notRestored = $"; not restored: changed again by {PatchName}";
        Assert.Contains(Assert.Single(run.Reports, report => report.Name == ReportFileNames.RemovalsFileName).Lines, line => line.Contains(notRestored, StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.Contains(notRestored, StringComparison.Ordinal));
    }

    /// <summary>
    /// The cleaned mod gives the crate another base and moves it 5 units; an earlier patcher of the group carries both and moves it
    /// to 8 units. The base goes back and the position stays, so the log must not say the object stands where it stood before.
    /// </summary>
    [Fact]
    public void AReplacedObjectWhosePositionAnEarlierPatcherMovedAgainIsLoggedAsRestoredExceptItsPosition()
    {
        var crate = default(FormKey);
        var settings = CrateSettings().WithReplacedObjects(process: true, SettingDefaults.ReplacedObjectsWhenRemoved);
        settings.DetailedLogAndReports = true;

        var (outcome, run) = FixtureRun.Capture(
            settings,
            Workers,
            dataFolder => CreateReplacedWorld(dataFolder, new Vector3(5, 0, 0), found => crate = found),
            state => OverrideInPatch(state, crate).Placement!.Position = new P3Float(8, 0, 0));

        var replaced = Assert.Single(outcome.Collected.Targets, target => target.Origin == TargetOrigin.Replaced);
        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(replaced.Id));
        Assert.Equal([KeyValuePair.Create(PlacedParts.Position, PatchName)], restored.EarlierVersion.LeftToLaterPlugins);
        Assert.Contains(run.Log, line => line.StartsWith("  Restored except its position, which stays as changed after Target.esp: ", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log, line => line.Contains("Restored where it stood", StringComparison.Ordinal));
    }

    /// <summary>The cleaned mod moves the crate and sets its radius: the data goes back with the position, but the base is the same.</summary>
    [Fact]
    public void TheNewBaseIsEmptyWhenTheDataWentBackButTheBaseDidNotChange()
    {
        var (outcome, run) = RunRestoring(RestoreMode.Restore, dataFolder => CreateCrateWorldMovedAnd(dataFolder, moved => moved.Radius = CleanedModRadius));

        var restored = Assert.IsType<Decision.Restored>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.True(restored.EarlierVersion.Undo.HasFlag(PlacedParts.BaseAndData));
        Assert.EndsWith(",0,0,0,0,1,,not checked,", Assert.Single(run.Reports, report => report.Name == ReportFileNames.RestoredFileName).Lines[1]);
        Assert.EndsWith(",0,0,0,0,1,", Assert.Single(run.Reports, report => report.Name == "changed-and-fitted-objects.csv").Lines[1]);
    }

    /// <summary>The crate world with the wall beside the new place, where the cleaned mod also sizes the crate.</summary>
    /// <param name="crateFound">Receives the crate's key.</param>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldMovedAndSized(string dataFolder, Action<FormKey> crateFound) =>
        CreateCrateWorldMovedAnd(dataFolder, moved =>
        {
            moved.Scale = CleanedModScale;
            crateFound(moved.FormKey);
        });

    /// <summary>The crate world with the wall beside the new place, where the cleaned mod also changes the moved crate as given.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldMovedAnd(string dataFolder, Action<IPlacedObject> change)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        change(world.Target.Mod.EnumerateMajorRecords<IPlacedObject>().Single(placed => placed.FormKey.ModKey == ModKey.FromNameAndExtension(BaseGamePlugin)));
        return world.LoadOrder;
    }

    /// <summary>The patch's override of the record, a copy of its winning version.</summary>
    private static IPlacedObject OverrideInPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey record) =>
        state.LinkCache.ResolveContext<IPlacedObject, IPlacedObjectGetter>(record).GetOrAddAsOverride(state.PatchMod);

    /// <summary>The crate world with a crate of the cleaned mod on top of the moved crate.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateCrateWorldWithAnOwnCrateOnTheMovedOne(string dataFolder)
    {
        var world = FillCrateWorld(CreateWorld(dataFolder), MovedTo, [NearMovedTo]);
        world.Target.Interior.Place(
            "T_OnTheMovedCrate", world.Target.AddModeledStatic("T_OnTopBase", FixtureMeshes.CrateModel), MovedTo + new Vector3(0, 0, FixtureMeshes.CrateSize));
        return world.LoadOrder;
    }
}
