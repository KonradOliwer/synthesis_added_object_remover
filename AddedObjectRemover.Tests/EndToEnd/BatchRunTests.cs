using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Batch mode through the entry, on a small world of its own: the first mod's crate and the second mod's pad stand
/// 5 units apart in one interior, so each mod's object is too close to the other's when its mod is cleaned up. In the
/// exterior cell, the third and the fourth mod each have a crate too close to a pad of a neighbouring mod, far apart.
/// The configs do not trust the mods loading before their cleaned mod, so the earlier mods' objects can cause removals, unless a
/// test says otherwise. Far from them in the interior, the mover mod moves a tree of the base game and has a crate too close to a
/// neighbour's pad, and the placer mod, loading after it, has a pad beside the tree's base-game place.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class BatchRunTests : IDisposable
{
    private const int Workers = 4;
    private const string FirstConfig = "first_mod";
    private const string SecondConfig = "second_mod";
    private const string GroupName = "my_group";
    private const string FirstPlugin = "First.esp";
    private const string SecondPlugin = "Second.esp";
    private const string ThirdConfig = "third_mod";
    private const string FourthConfig = "fourth_mod";
    private const string ThirdPlugin = "Third.esp";
    private const string FourthPlugin = "Fourth.esp";
    private const string NeighbourPlugin = "Neighbours.esp";
    private const float ThirdCrateCorner = 1000f;
    private const float FourthCrateCorner = 3000f;
    private const string MoverConfig = "mover_mod";
    private const string PlacerConfig = "placer_mod";
    private const string MoverPlugin = "Mover.esp";
    private const string PlacerPlugin = "Placer.esp";
    private static readonly Vector3 TreeAt = new(20000, 0, 0);
    private static readonly Vector3 TreeMovedTo = new(20000, 600, 0);
    private static readonly Vector3 MoverCrateAt = new(20000, 3000, 0);
    private const string Banner = "=== Added Object Remover ===";
    private const string DoneLinePrefix = "Done";
    private const string RunSummaryPrefix = "Objects of ";

    private const string NothingNotJudged =
        "not judged (winning version from a later plugin, removed by a later plugin, deleted, start disabled, or without a valid position or rotation): 0; ";
    private const string ReportsFolder = Program.ReportsFolderName;
    private const float PadCentre = 75f;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-batch-");
    private readonly string _dataFolder;
    private readonly string _outputFolder;
    private readonly World _world;

    public BatchRunTests()
    {
        _dataFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Data")).FullName;
        _outputFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Output")).FullName;
        _world = CreateWorld(_dataFolder);
    }

    private FormKey Crate => _world.Crate;

    private FormKey Pad => _world.Pad;

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void TheSecondConfigWritesIntoTheCellCopyTheFirstOneMade()
    {
        WriteGroup(ThirdConfig, FourthConfig);
        WriteConfig(ThirdConfig, ThirdPlugin);
        WriteConfig(FourthConfig, FourthPlugin);

        var run = Run();

        Assert.Single(run.PatchMod.Worldspaces);
        var cell = Assert.Single(run.PatchMod.EnumerateMajorRecords<ICellGetter>());
        Assert.Equal([_world.ThirdCrate, _world.FourthCrate], cell.EnumeratePlaced().Select(entry => entry.Record.FormKey));
    }

    /// <summary>Nothing passes between the runs: the second one reads the first one's removal from the patch.</summary>
    [Fact]
    public void TheSecondConfigSeesTheObjectTheFirstOneRemovedThroughThePatchAsGone()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteConfig(SecondConfig, SecondPlugin);

        var run = Run();

        Assert.Equal([Crate], run.RemovedRecords());
        Assert.Equal(
            [
                $"Objects of First.esp: 1; already removed by earlier patchers in this group: 0; {NothingNotJudged}judged in this run: 1; removed in this run: 1.",
                $"Objects of Second.esp: 1; already removed by earlier patchers in this group: 0; {NothingNotJudged}judged in this run: 1; removed in this run: 0.",
            ],
            run.Log.Where(line => line.StartsWith(RunSummaryPrefix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The mover's run removes its crate, which clears the interior, so it puts the tree back where the base game has it. The
    /// placer's run trusts the mover, which loads before it: the tree as the patch holds it equals the base game's, so it is no
    /// earlier patcher's change and the placer's pad beside it stays.
    /// </summary>
    [Fact]
    public void AnObjectAnEarlierRunPutBackAsTheBaseGameHasItRemovesNothingInTheNextRun()
    {
        WriteGroup(MoverConfig, PlacerConfig);
        WriteConfig(MoverConfig, MoverPlugin, trustModsLoadingBeforeCleanedMod: true);
        WriteConfig(PlacerConfig, PlacerPlugin, trustModsLoadingBeforeCleanedMod: true);

        var run = Run();

        Assert.Equal(TreeAt, run.PositionOf(_world.Tree));
        Assert.Equal([_world.MoverCrate], run.RemovedRecords());
    }

    [Fact]
    public void TheSecondConfigRemovesItsObjectWhenItRunsAlone()
    {
        WriteGroup(SecondConfig);
        WriteConfig(SecondConfig, SecondPlugin);

        var run = Run();

        Assert.Equal([Pad], run.RemovedRecords());
    }

    [Fact]
    public void EachConfigWritesItsReportFilesToASubfolderNamedAfterIt()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteConfig(SecondConfig, SecondPlugin);

        Run();

        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_outputFolder, ReportsFolder, FirstConfig)));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_outputFolder, ReportsFolder, SecondConfig)));
    }

    [Fact]
    public void ABatchOfTwoPrintsTheBannerOnceAndFinishesEachRun()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteConfig(SecondConfig, SecondPlugin);

        var run = Run();

        Assert.Single(run.Log, line => line == Banner);
        Assert.Equal(2, run.Log.Count(line => line.StartsWith(DoneLinePrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void AMissingConfigFileFailsTheRunBeforeAnythingRunsOrIsWritten()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);

        var run = RunFailing();

        Assert.Contains("Config file not found", run.Message);
        AssertNothingRanOrWritten(run);
    }

    [Fact]
    public void TwoConfigsWithTheSameTargetFailTheRunBeforeAnythingRunsOrIsWritten()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteConfig(SecondConfig, FirstPlugin);

        var run = RunFailing();

        Assert.Contains("both clean up", run.Message);
        AssertNothingRanOrWritten(run);
    }

    [Fact]
    public void AnInvalidValueInTheSecondConfigFailsTheRunBeforeAnythingRunsOrIsWritten()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteFile(Path.Combine("configs", SecondConfig + ".json"), "{\"CleanedMod\": \"Second.esp\", \"TooClose\": {\"NpcHandling\": \"Sometimes\"}}");

        var run = RunFailing();

        Assert.Contains("Invalid setting in config file", run.Message);
        Assert.Contains(SecondConfig + ".json", run.Message);
        AssertNothingRanOrWritten(run);
    }

    [Fact]
    public void AMissingPluginsFileFailsTheRunBeforeAnythingRunsOrIsWritten()
    {
        WriteGroup(FirstConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        var missing = Path.Combine(_outputFolder, "Missing.txt");

        var run = RunFailing(_ => missing);

        Assert.Contains(missing, run.Message);
        AssertNothingRanOrWritten(run);
    }

    [Fact]
    public void AMalformedPluginsFileLineFailsTheRunNamingTheFileAndTheLineBeforeAnythingRunsOrIsWritten()
    {
        WriteGroup(FirstConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        var malformed = Path.Combine(_outputFolder, "Malformed.txt");
        File.WriteAllLines(malformed, ["*Skyrim.esm", "NoExtension"]);

        var run = RunFailing(_ => malformed);

        Assert.Equal(
            $"The plugin list file {malformed} has a malformed entry at line 2. Correct or remove that line. Nothing was run and no patch was written.",
            run.Message);
        AssertNothingRanOrWritten(run);
    }

    private void AssertNothingRanOrWritten(FailedRun run)
    {
        Assert.Equal([Banner], run.Log);
        Assert.Empty(run.Patch);
        Assert.False(Directory.Exists(Path.Combine(_outputFolder, ReportsFolder)));
    }

    private sealed record FinishedRun(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, ISkyrimModGetter PatchMod)
    {
        public IReadOnlyList<FormKey> RemovedRecords() =>
            [.. Patch.Where(line => line.Contains("disabled True")).Select(line => FormKey.Factory(line.Split(' ')[0]))];

        public Vector3 PositionOf(FormKey record)
        {
            var position = PatchMod.EnumerateMajorRecords<ICellGetter>()
                .SelectMany(cell => cell.EnumeratePlaced())
                .Single(entry => entry.Record.FormKey == record)
                .Record.Placement!.Position;
            return new Vector3(position.X, position.Y, position.Z);
        }
    }

    private sealed record FailedRun(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, string Message);

    private FinishedRun Run()
    {
        var state = CreateState();
        var log = PipelineRun.CaptureConsole(() => Program.RunPatch(state, BatchSettings, Workers, FixturePatcherState.WritePluginsFile(state)));
        return new FinishedRun(log, PatchDump.Describe(state.PatchMod), state.PatchMod);
    }

    private FailedRun RunFailing() => RunFailing(FixturePatcherState.WritePluginsFile);

    private FailedRun RunFailing(Func<IPatcherState<ISkyrimMod, ISkyrimModGetter>, string> pluginsFileOf)
    {
        var state = CreateState();
        BatchStoppedException? stopped = null;
        var log = PipelineRun.CaptureConsole(() => stopped = Assert.Throws<BatchStoppedException>(() => Program.RunPatch(state, BatchSettings, Workers, pluginsFileOf(state))));
        return new FailedRun(log, PatchDump.Describe(state.PatchMod), stopped!.Message);
    }

    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState() =>
        FixturePatcherState.Create(_world.LoadOrder, _dataFolder, Path.Combine(_outputFolder, FixturePatcherState.PatchModKey.FileName));

    private static IModeSettingsValues BatchSettings() =>
        new Settings { Mode = PatchMode.Batch, DetailedLogAndReports = true, Batch = new BatchSettings { GroupFileName = GroupName } };

    private void WriteGroup(params string[] configs) =>
        WriteFile(Path.Combine("groups", GroupName + ".json"), "[" + string.Join(", ", configs.Select(name => $"\"{name}\"")) + "]");

    private void WriteConfig(string name, string target, bool trustModsLoadingBeforeCleanedMod = false) =>
        WriteFile(
            Path.Combine("configs", name + ".json"),
            $$"""
            {
              "CleanedMod": "{{target}}",
              "Trust": { "TrustModsLoadingBeforeCleanedMod": {{(trustModsLoadingBeforeCleanedMod ? "true" : "false")}} },
              "TooClose": {
                "BySize": { "Use": true, "SizeMultiplier": 0.1 },
                "ByDistance": { "Use": true, "DistanceInUnits": 32 }
              }
            }
            """);

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_dataFolder, "synthesis_added_object_remover", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed record World(
        IReadOnlyList<ISkyrimMod> LoadOrder, FormKey Crate, FormKey Pad, FormKey ThirdCrate, FormKey FourthCrate, FormKey Tree, FormKey MoverCrate);

    private static World CreateWorld(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod("Skyrim.esm");
        var first = new FixtureMod(FirstPlugin, baseGame);
        var second = new FixtureMod(SecondPlugin, baseGame);
        var third = new FixtureMod(ThirdPlugin, baseGame);
        var fourth = new FixtureMod(FourthPlugin, baseGame);
        var mover = new FixtureMod(MoverPlugin, baseGame);
        var placer = new FixtureMod(PlacerPlugin, baseGame);
        var neighbours = new FixtureMod(NeighbourPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        foreach (var mod in new[] { first, second, third, fourth, mover, placer, neighbours }) spaces.AddOverridesTo(mod);

        var crate = first.Interior.Place("F_Crate", first.AddModeledStatic("F_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);
        var pad = second.Interior.Place("S_Pad", second.AddModeledStatic("S_PadBase", FixtureMeshes.PadModel), new Vector3(PadCentre, 0, 0));
        var thirdCrate = PlaceExteriorCrateNextToAPad(third, neighbours, ThirdCrateCorner);
        var fourthCrate = PlaceExteriorCrateNextToAPad(fourth, neighbours, FourthCrateCorner);
        var tree = PlaceTreeTheMoverMovesWithAPlacerPadBesideIt(baseGame, mover, placer);
        var moverCrate = PlaceCrateNextToAPad(mover, neighbours, mod => mod.Interior, MoverCrateAt);
        return new World(
            [baseGame.Mod, first.Mod, second.Mod, third.Mod, fourth.Mod, mover.Mod, placer.Mod, neighbours.Mod],
            crate.FormKey, pad.FormKey, thirdCrate.FormKey, fourthCrate.FormKey, tree.FormKey, moverCrate.FormKey);
    }

    private static PlacedObject PlaceTreeTheMoverMovesWithAPlacerPadBesideIt(FixtureMod baseGame, FixtureMod mover, FixtureMod placer)
    {
        var tree = baseGame.Interior.Place("B_Tree", baseGame.AddModeledStatic("B_TreeBase", FixtureMeshes.CrateModel), TreeAt);
        mover.Interior.PlaceCopyOf(tree).Placement!.Position = new P3Float(TreeMovedTo.X, TreeMovedTo.Y, TreeMovedTo.Z);
        placer.Interior.Place("P_Pad", placer.AddModeledStatic("P_PadBase", FixtureMeshes.PadModel), TreeAt + new Vector3(PadCentre, 0, 0));
        return tree;
    }

    /// <summary>The crate stands on the terrain at (corner, corner).</summary>
    private static PlacedObject PlaceExteriorCrateNextToAPad(FixtureMod owner, FixtureMod neighbour, float corner) =>
        PlaceCrateNextToAPad(owner, neighbour, mod => mod.Exterior, new Vector3(corner, corner, FixtureSpaces.TerrainHeight));

    /// <summary>The neighbouring mod's pad stands as close to the crate as the interior's pad to the first mod's crate.</summary>
    /// <param name="cellOf">The cell of a mod both objects stand in.</param>
    private static PlacedObject PlaceCrateNextToAPad(FixtureMod owner, FixtureMod neighbour, Func<FixtureMod, FixtureCell> cellOf, Vector3 at)
    {
        var crate = cellOf(owner).Place(owner.Key.Name + "_Crate", owner.AddModeledStatic(owner.Key.Name + "_CrateBase", FixtureMeshes.CrateModel), at);
        cellOf(neighbour).Place(
            owner.Key.Name + "_Pad", neighbour.AddModeledStatic(owner.Key.Name + "_PadBase", FixtureMeshes.PadModel), at + new Vector3(PadCentre, 0, 0));
        return crate;
    }
}
