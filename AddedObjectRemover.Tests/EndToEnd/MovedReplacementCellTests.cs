using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using static AddedObjectRemover.Tests.EndToEnd.MovedReplacementScenes;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Whole runs where the cleaned mod moved a base-game object into another exterior cell, and a later plugin deletes one of the cells.</summary>
public class MovedReplacementCellTests
{
    private const int HomeGridX = 5;
    private const int EastGridX = HomeGridX + 1;

    /// <summary>A point inside the grid square of the home cell, and one inside the square of the cell east of it.</summary>
    private static readonly Vector3 InTheHomeCell = new(HomeGridX * ExteriorGrid.CellSize + 1000, 1000, 0);
    private static readonly Vector3 InTheEastCell = new(EastGridX * ExteriorGrid.CellSize + 1000, 1000, 0);

    /// <summary>A point on the border between the home cell and the east cell, far from the crate.</summary>
    private static readonly Vector3 AtTheBorder = new(EastGridX * ExteriorGrid.CellSize, 3000, 0);

    private const string BorderCrateEditorId = "T_BorderCrate";
    private const string BorderWallEditorId = "C_BorderWall";

    private const string HomeCellEditorId = "AorHomeCell";
    private const string EastCellEditorId = "AorEastCell";

    /// <summary>The cleaned mod moved the crate into the next exterior cell; shrunk there, the patch keeps it in the cell the cleaned mod put it in.</summary>
    [Fact]
    public void AReplacementMovedIntoAnotherCellIsShrunkInThatCell()
    {
        string? cellOfTheWrittenCrate = null;
        var outcome = FixtureRun.Execute(RemoveAfterResizing(ShrinkSettings()), Workers, CreateTwoCellWorld);
        RunTwoCellWorld(patch => cellOfTheWrittenCrate = CellHoldingBaseGameObject(patch));

        Assert.IsType<Decision.Fitted>(outcome.Final.Of(ReplacementOf(outcome).Id));
        Assert.Equal(EastCellEditorId, cellOfTheWrittenCrate);
    }

    /// <summary>
    /// The cleaned mod moved the crate into the next exterior cell, and the city mod deletes the crate's home cell, which the restore
    /// would move it back into. With the restore where the cleaned mod was removed on or off, and with either restoring mode, the
    /// crate is disabled in the cell it stands in instead, and the log names the deleted cell.
    /// </summary>
    [Theory]
    [InlineData(false, RestoreMode.Restore)]
    [InlineData(true, RestoreMode.Restore)]
    [InlineData(false, RestoreMode.RestoreIfNoCollision)]
    [InlineData(true, RestoreMode.RestoreIfNoCollision)]
    public void AMovedObjectWhoseRestoreWouldGoIntoADeletedCellIsDisabledWhereItStands(bool restoreWhereRemoved, RestoreMode mode)
    {
        var (outcome, run) = RunTwoCellWorldRestoring(HomeCellEditorId, restoreWhereRemoved, mode);

        var crate = ReplacementOf(outcome);
        Assert.IsType<Decision.Removed>(outcome.Final.Of(crate.Id));
        var cellDeleted = Assert.IsType<WhenRemoved.DisableInsteadOfRestore>(outcome.Final.WhenRemovedOf(crate.Id)).CellDeleted;
        Assert.Equal((HomeCellEditorId, new PluginName(CityPlugin)), (cellDeleted?.Cell.EditorId, cellDeleted?.DeletedBy));
        Assert.Contains(
            run.Log,
            line => line.Contains($"; not restored: the cell it would go back into, {HomeCellEditorId} [", StringComparison.Ordinal)
                && line.Contains($"is deleted by {CityPlugin}", StringComparison.Ordinal));
        Assert.StartsWith($"{crate.Key.ToFormKey()} in {crate.Cell!.Key.ToFormKey()} (temporary): disabled True", Assert.Single(run.Patch), StringComparison.Ordinal);
    }

    /// <summary>
    /// The city mod deletes the cell the cleaned mod moved the crate into. The crate is still judged and the city wall beside it in
    /// that cell still makes it too close, but it is kept, since the patch writes nothing into a deleted cell; the log names the cell
    /// and the plugin. The cleaned mod's own crate across the border, in the home cell, is removed for the city wall standing in the
    /// deleted cell, and its disable is the patch's only record. With the restore where the cleaned mod was removed on or off, and
    /// with either restoring mode.
    /// </summary>
    [Theory]
    [InlineData(false, RestoreMode.Restore)]
    [InlineData(true, RestoreMode.Restore)]
    [InlineData(false, RestoreMode.RestoreIfNoCollision)]
    [InlineData(true, RestoreMode.RestoreIfNoCollision)]
    public void AMovedObjectInACellALaterPluginDeletesIsKeptAndObjectsInThatCellStillCauseRemovals(bool restoreWhereRemoved, RestoreMode mode)
    {
        var (outcome, run) = RunTwoCellWorldRestoring(EastCellEditorId, restoreWhereRemoved, mode, withAnOwnCrateAcrossTheBorder: true);

        var crate = ReplacementOf(outcome);
        var kept = Assert.IsType<Decision.Kept>(outcome.Final.Of(crate.Id));
        Assert.Equal(KeepKind.CellDeleted, kept.KeepReason.Kind);
        Assert.Equal((EastCellEditorId, new PluginName(CityPlugin)), (kept.KeepReason.CellDeleted?.Cell.EditorId, kept.KeepReason.CellDeleted?.DeletedBy));
        Assert.Contains(run.Log, line => line.StartsWith("  Kept, in a deleted cell: B_Crate ", StringComparison.Ordinal) && line.EndsWith($": deleted by {CityPlugin}.", StringComparison.Ordinal));
        var borderCrate = Assert.Single(outcome.Collected.Targets, target => target.EditorId == BorderCrateEditorId);
        Assert.IsType<Decision.Removed>(outcome.Final.Of(borderCrate.Id));
        Assert.Contains(run.Log, line => line.StartsWith($"  Removed {BorderCrateEditorId} ", StringComparison.Ordinal) && line.Contains(BorderWallEditorId, StringComparison.Ordinal));
        Assert.StartsWith($"{borderCrate.Key.ToFormKey()} in {borderCrate.Cell!.Key.ToFormKey()} (temporary): disabled True", Assert.Single(run.Patch), StringComparison.Ordinal);
    }

    [Fact]
    public void AMovedReplacementIsJudgedInASpaceWhereTheCleanedModHasNoObjectOfItsOwn()
    {
        var outcome = FixtureRun.Execute(CrateSettings(), Workers, CreateTwoCellWorld);

        var replacement = ReplacementOf(outcome);
        Assert.DoesNotContain(outcome.Collected.Targets, target => target.Origin == TargetOrigin.CleanedMod && target.SpaceKey == replacement.SpaceKey);
        Assert.NotNull(outcome.Final.Of(replacement.Id));
    }

    /// <summary>The Editor ID of the patch's cell whose temporary list holds the base game's object.</summary>
    private static string? CellHoldingBaseGameObject(ISkyrimModGetter patch) =>
        patch.EnumerateMajorRecords<ICellGetter>()
            .Single(cell => cell.Temporary.Any(placed => placed.FormKey.ModKey == ModKey.FromNameAndExtension(BaseGamePlugin)))
            .EditorID;

    /// <param name="deletedByTheCityMod">The Editor ID of the cell the city mod's copy deletes.</param>
    /// <param name="restoreWhereRemoved">Whether the restore where the cleaned mod was removed is on; off, its four switches are off.</param>
    /// <param name="mode">How the moved crate is restored when it would be removed.</param>
    private static (RunOutcome Outcome, RunOutput Run) RunTwoCellWorldRestoring(
        string deletedByTheCityMod, bool restoreWhereRemoved, RestoreMode mode, bool withAnOwnCrateAcrossTheBorder = false)
    {
        var settings = CrateSettings().WithMovedObjects(mode);
        settings.DetailedLogAndReports = true;
        var restore = settings.Standard.RestoreWhereRemoved;
        (restore.RestoreOtherModsObjects, restore.RestoreTerrain, restore.RestoreCells, restore.RestoreNavmeshes) =
            (restoreWhereRemoved, restoreWhereRemoved, restoreWhereRemoved, restoreWhereRemoved);
        return FixtureRun.Capture(settings, Workers, dataFolder => CreateTwoCellWorld(dataFolder, deletedByTheCityMod, withAnOwnCrateAcrossTheBorder));
    }

    private static void RunTwoCellWorld(Action<ISkyrimModGetter> inspectPatch) =>
        FixtureRun.Run(RemoveAfterResizing(ShrinkSettings()), Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreateTwoCellWorld, inspectPatch: inspectPatch);

    /// <summary>
    /// The base game's crate in its own exterior cell; the cleaned mod moves it into the cell east of it, next to a city wall. The
    /// cleaned mod's own crate is in the interior, so the worldspace holds no object of its own. Each plugin holds both cells.
    /// </summary>
    private static IReadOnlyList<ISkyrimMod> CreateTwoCellWorld(string dataFolder) => CreateTwoCellWorld(dataFolder, deletedByTheCityMod: null, withAnOwnCrateAcrossTheBorder: false);

    /// <param name="deletedByTheCityMod">The Editor ID of the cell the city mod's copy deletes; none when it deletes none.</param>
    /// <param name="withAnOwnCrateAcrossTheBorder">
    /// Whether the cleaned mod also places a crate of its own in the home cell, at the border to the east cell, too close to a city
    /// wall just across the border in the east cell.
    /// </param>
    private static IReadOnlyList<ISkyrimMod> CreateTwoCellWorld(string dataFolder, string? deletedByTheCityMod, bool withAnOwnCrateAcrossTheBorder)
    {
        var world = CreateWorld(dataFolder);
        var (homeKey, eastKey) = (world.BaseGame.NextKey(), world.BaseGame.NextKey());
        var cells = new[] { world.BaseGame, world.Target, world.City }
            .ToDictionary(mod => mod, mod => (Home: mod.AddExteriorCell(homeKey, HomeCellEditorId, HomeGridX), East: mod.AddExteriorCell(eastKey, EastCellEditorId, EastGridX)));
        var crate = cells[world.BaseGame].Home.Place("B_Crate", world.BaseGame.AddModeledStatic("B_CrateBase", FixtureMeshes.CrateModel), InTheHomeCell);
        MoveCopy(cells[world.Target].East.PlaceCopyOf(crate), InTheEastCell);
        PlaceWalls(world.City, cells[world.City].East, [InTheEastCell + new Vector3(75, 0, 0)]);
        if (withAnOwnCrateAcrossTheBorder)
        {
            cells[world.Target].Home.Place(BorderCrateEditorId, world.Target.AddModeledStatic("T_BorderCrateBase", FixtureMeshes.CrateModel), AtTheBorder - new Vector3(38, 0, 0));
            cells[world.City].East.Place(BorderWallEditorId, world.City.AddModeledStatic("C_BorderWallBase", FixtureMeshes.PadModel), AtTheBorder + new Vector3(37, 0, 0));
        }
        foreach (var cell in new[] { cells[world.City].Home, cells[world.City].East }.Where(cell => cell.Record.EditorID == deletedByTheCityMod)) cell.Record.IsDeleted = true;
        return world.LoadOrder;
    }
}
