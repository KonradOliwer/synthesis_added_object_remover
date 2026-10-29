using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Copies of the patcher chained in one Synthesis group, each in Standard mode, give what one Batch run of the same configs gives.
/// The chain runs as Synthesis runs it: each copy's output is written to its file, and the next copy starts from that file,
/// imported as its patch. All runs go through the entry, on a small world of their own with two cleaned mods whose objects meet.
/// In the interior, a crate of the first mod and a pad of the second stand 5 units apart twice: once both with a base game lever
/// as enable parent, once both persistent; whichever mod runs first removes its two objects, and the other run then keeps its
/// own. In the exterior, a marker of the first mod stands inside a building of the second mod and is kept because a plugin that
/// requires the first mod lists it, so the first mod's run moves it, and the second mod's run sees it as another mod's object.
/// The configs do not trust the mods loading before their cleaned mod, so the first mod's objects count in the second mod's run.
/// A second world adds a plugin below the patch, which the final world opens from the Data folder, and switches the changed-terrain
/// rule on: the plugin raises the terrain under a crate of each mod and places a pad 5 units from another crate of the first mod.
/// A third world has both mods move the same crate of the base game and a city mod put a wall beside each place the crate stands in
/// (see <see cref="ChainedStandardRunsGiveTheResultOfOneBatchRunWhenBothModsMovedTheSameObjectAndEachRunRestoresItToTheVersionBelowItsOwnCleanedMod"/>).
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class ChainEqualsBatchTests : IDisposable
{
    private const int Workers = 4;
    private const string GroupName = "chained_mods";
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string FirstPlugin = "First.esp";
    private const string SecondPlugin = "Second.esp";
    private const string KeeperPlugin = "Keeper.esp";
    private const string BelowPlugin = "Below.esp";
    private const string FirstCrateOnRaisedTerrain = "First_CrateOnRaisedTerrain";
    private const string SecondCrateOnRaisedTerrain = "Second_CrateOnRaisedTerrain";
    private const string FirstCrateNextToBelowPad = "First_CrateNextToBelowPad";
    private const string CityPlugin = "City.esp";
    private const string ChainRunFolderPrefix = "chain-";
    private const string BatchRunFolder = "batch";
    private const string Banner = "=== Added Object Remover ===";
    private const string ConfigDelimiterPrefix = "=== Config ";
    private const string RunSummaryPrefix = "Objects of ";
    private const string ReportNamePrefix = "## report ";
    private const string RemovedLineMark = "disabled True";
    private const string KeptLineMark = "disabled False";
    private const string PersistentLineMark = "(persistent)";
    private const string RemovedDepthMark = "-30000.00";

    /// <summary>The player reference, which the removal of an object with an enable parent writes as its new enable parent.</summary>
    private const string PlayerReference = "000014:Skyrim.esm";

    /// <summary>Each mod places three objects: one with the lever as enable parent, one persistent, and the marker or the building.</summary>
    private const int ObjectsPerMod = 3;

    /// <summary>The pad's near side 5 units from the 100-unit crate: inside the fixed distance margin of 32 units.</summary>
    private const float PadCentre = 75f;

    private const float SceneSpacing = 1000f;

    /// <summary>The three places of the crate both mods move stand in a row of the interior that no other scene uses.</summary>
    private static readonly Vector3 CrateInBaseGame = new(0, 5 * SceneSpacing, 0);

    private static readonly Vector3 CrateInFirstMod = CrateInBaseGame + new Vector3(SceneSpacing, 0, 0);
    private static readonly Vector3 CrateInSecondMod = CrateInBaseGame + new Vector3(2 * SceneSpacing, 0, 0);

    /// <summary>The crates of the world with a plugin below the patch stand in the exterior cells beside the shared one, at these distances from the cell's west edge.</summary>
    private const float FirstCrateX = SceneSpacing;

    private const float SecondCrateX = 2 * SceneSpacing;
    private const int RaisedTerrainCell = 1;
    private const int PaddedCell = 2;

    /// <summary>A LAND height step in game units.</summary>
    private const float HeightStepUnits = 8f;

    /// <summary>8 steps of 8 units: 64 units, above the default tolerance of 16 units.</summary>
    private const float RaisedSteps = 8f;

    private const float MarkerHeight = 100f;
    private const float SizeMultiplier = 0.1f;
    private const float DistanceInUnits = 32f;

    private static readonly Config First = new("first_mod", FirstPlugin);
    private static readonly Config Second = new("second_mod", SecondPlugin);

    /// <summary>The lines a config's section of a batch log holds before that config's run: the notes on its keys and settings.</summary>
    private static readonly string[] ConfigNotePrefixes = ["Ignored in ", "Not set in ", "Set in ", "  "];

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-chain-");
    private readonly string _dataFolder;
    private readonly World _world;

    public ChainEqualsBatchTests()
    {
        _dataFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Data")).FullName;
        _world = CreateWorld(_dataFolder, withPluginBelowPatch: false);
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <summary>The decision lists are compared through the report files, which list every removal and every kept object with its reason.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedStandardRunsGiveThePatchLogAndReportsOfOneBatchRunOfTheirConfigs(bool secondModFirst)
    {
        var order = Order(secondModFirst);

        var chain = RunChain(order, _world);
        var batch = RunBatch(order, _world);

        AssertChainEqualsBatch(order, chain, batch);
    }

    /// <summary>
    /// Only the plugin below the patch causes the removals checked here: its terrain change under a crate of each mod, and its pad
    /// next to another crate of the first mod.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedStandardRunsGiveTheResultOfOneBatchRunWhenAPluginBelowThePatchChangesTheTerrainAndPlacesAnObject(bool secondModFirst)
    {
        var world = CreateWorld(_dataFolder, withPluginBelowPatch: true);
        var order = Order(secondModFirst);

        var chain = RunChain(order, world);
        var batch = RunBatch(order, world);

        AssertChainEqualsBatch(order, chain, batch);
        Assert.All(world.RemovedBecauseOfBelowPatch, record => Assert.Contains(RemovedLineMark, LineOf(batch.Patch, record)));
        Assert.Contains(batch.Log, line => IsTerrainRemoval(line, FirstCrateOnRaisedTerrain));
        Assert.Contains(batch.Log, line => IsTerrainRemoval(line, SecondCrateOnRaisedTerrain));
    }

    public static TheoryData<bool, RestoreMode, int> MovedCrateCases()
    {
        var cases = new TheoryData<bool, RestoreMode, int>();
        foreach (var secondModFirst in new[] { false, true })
        {
            foreach (var mode in new[] { RestoreMode.Restore, RestoreMode.RestoreIfNoCollision })
            {
                foreach (var workers in new[] { 1, Workers }) cases.Add(secondModFirst, mode, workers);
            }
        }

        return cases;
    }

    /// <summary>
    /// The base game puts a crate at A in the interior, the first mod moves it to B, the second mod (loading after it, not needing
    /// it) moves it to C, and a city mod puts a wall beside B and beside C. The crate stands too close to the wall at C and, once
    /// the second mod's run wrote it to B, at B. Each run restores it to the version just below its own cleaned mod: the second
    /// mod's run to B, the first mod's run to A. When the first mod runs first, the second mod wins the crate and the first mod's
    /// run has nothing to restore; the second mod's run then restores it to B. When the second mod runs first, the first mod's run
    /// judges the patch's crate at B against the base game's at A. The wall stood at B without either mod, so it is no collision.
    /// </summary>
    [Theory]
    [MemberData(nameof(MovedCrateCases))]
    public void ChainedStandardRunsGiveTheResultOfOneBatchRunWhenBothModsMovedTheSameObjectAndEachRunRestoresItToTheVersionBelowItsOwnCleanedMod(
        bool secondModFirst, RestoreMode mode, int workers)
    {
        var world = CreateWorld(_dataFolder, withPluginBelowPatch: false, withMovedCrate: true) with { WhenMovedObjectsAreRemoved = mode };
        var order = Order(secondModFirst);

        var chain = RunChain(order, world, workers);
        var batch = RunBatch(order, world, workers);

        AssertChainEqualsBatch(order, chain, batch);
        var crate = LineOf(batch.Patch, world.MovedCrate!.Value);
        Assert.Contains(RestoredAt(secondModFirst ? CrateInBaseGame : CrateInFirstMod), crate);
        Assert.Contains(KeptLineMark, crate);
    }

    private static string RestoredAt(Vector3 position) =>
        FormattableString.Invariant($"position ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");

    private static void AssertChainEqualsBatch(IReadOnlyList<Config> order, IReadOnlyList<StandardRun> chain, BatchRun batch)
    {
        Assert.Equal(batch.Patch, chain[^1].Patch);
        Assert.Equal(batch.PatchDescription, chain[^1].PatchDescription);
        Assert.Contains(ReportNamePrefix + ReportFileNames.RemovalsFileName, chain[0].Reports);
        for (var index = 0; index < order.Count; index++)
        {
            Assert.Equal(batch.RunLogs[index], chain[index].RunLog);
            Assert.Equal(batch.Reports[index], chain[index].Reports);
        }
    }

    private static bool IsTerrainRemoval(string line, string editorId) =>
        line.Contains(editorId, StringComparison.Ordinal) && line.EndsWith($"terrain changed by {BelowPlugin} (height)", StringComparison.Ordinal);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InABatchTheSecondRunSeesTheObjectsTheFirstRunRemovedAsGoneAndTheMarkerAsAnotherModsObject(bool secondModFirst)
    {
        var order = Order(secondModFirst);
        var (earlier, later) = (order[0], order[1]);

        var batch = RunBatch(order, _world);

        Assert.Equal(Sorted(ObjectsOf(earlier)), Sorted(RemovedRecords(batch.Patch)));
        Assert.Contains(RemovedLineMark, LineOf(batch.Patch, _world.NextToLever[earlier.Plugin]));
        Assert.Contains($"enable parent {PlayerReference} ", LineOf(batch.Patch, _world.NextToLever[earlier.Plugin]));
        Assert.Contains($"{PersistentLineMark}: {RemovedLineMark}", LineOf(batch.Patch, _world.Persistent[earlier.Plugin]));
        Assert.DoesNotContain(RemovedDepthMark, LineOf(batch.Patch, _world.Persistent[earlier.Plugin]));
        Assert.Contains(KeptLineMark, LineOf(batch.Patch, _world.Marker));
        Assert.Single(batch.Log, line => line == MarkerMovesLine(1));
        Assert.Equal(
            [
                RunSummary(earlier, alreadyRemoved: 0, judged: ObjectsPerMod, removed: ObjectsOf(earlier).Count),
                RunSummary(later, alreadyRemoved: 0, judged: ObjectsPerMod, removed: 0),
            ],
            batch.Log.Where(line => line.StartsWith(RunSummaryPrefix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// A batch cannot clean up one mod twice, but a chain can: the second run judges the marker where the first run moved it, out
    /// of the building, so it has nothing to move; the objects the first run removed count as already removed.
    /// </summary>
    [Fact]
    public void ASecondStandardRunOfTheSameCleanedModJudgesTheMarkerWhereTheFirstRunMovedIt()
    {
        var chain = RunChain([First, First], _world);

        Assert.Contains(MarkerMovesLine(1), chain[0].RunLog);
        Assert.Contains(MarkerMovesLine(0), chain[1].RunLog);
        Assert.Equal(LineOf(chain[0].Patch, _world.Marker), LineOf(chain[1].Patch, _world.Marker));
        Assert.Contains(
            RunSummary(First, alreadyRemoved: ObjectsOf(First).Count, judged: ObjectsPerMod - ObjectsOf(First).Count, removed: 0),
            chain[1].RunLog);
    }

    /// <param name="Name">The config file's name, without .json.</param>
    private sealed record Config(string Name, string Plugin);

    /// <param name="RunLog">The run's log without the banner, with the run's folder masked.</param>
    /// <param name="Reports">Each report file as its name line and its lines.</param>
    /// <param name="PatchFile">The output file the run wrote, which the next run of the chain starts from.</param>
    private sealed record StandardRun(
        IReadOnlyList<string> RunLog, IReadOnlyList<string> Patch, string? PatchDescription, IReadOnlyList<string> Reports, string PatchFile);

    /// <param name="Log">The whole log, with the run's folder masked.</param>
    /// <param name="RunLogs">By config, in run order: its run's lines, without the lines that open its section.</param>
    /// <param name="Reports">By config, in run order: each report file of its folder as its name line and its lines.</param>
    private sealed record BatchRun(
        IReadOnlyList<string> Log,
        IReadOnlyList<IReadOnlyList<string>> RunLogs,
        IReadOnlyList<string> Patch,
        string? PatchDescription,
        IReadOnlyList<IReadOnlyList<string>> Reports);

    /// <param name="NextToLever">By plugin: its object with the lever as enable parent, 5 units from the other mod's.</param>
    /// <param name="Persistent">By plugin: its persistent object, 5 units from the other mod's.</param>
    /// <param name="Marker">The first mod's marker inside the second mod's building.</param>
    /// <param name="BelowPatch">The plugins the plugin list names below the patch, which are files in the Data folder; none in the small world.</param>
    /// <param name="RemovedBecauseOfBelowPatch">The objects only a plugin below the patch makes the runs remove; none in the small world.</param>
    /// <param name="RemoveOnChangedTerrain">Whether the world's configs switch the changed-terrain rule on; only the world with a plugin below the patch has terrain changes.</param>
    /// <param name="MovedCrate">The base game's crate that both mods move; none in the worlds without that scene.</param>
    /// <param name="WhenMovedObjectsAreRemoved">What the world's configs do with a moved object that a step would remove.</param>
    private sealed record World(
        IReadOnlyList<ISkyrimMod> LoadOrder,
        IReadOnlyDictionary<string, FormKey> NextToLever,
        IReadOnlyDictionary<string, FormKey> Persistent,
        FormKey Marker,
        IReadOnlyList<ISkyrimMod> BelowPatch,
        IReadOnlyList<FormKey> RemovedBecauseOfBelowPatch,
        bool RemoveOnChangedTerrain,
        FormKey? MovedCrate = null,
        RestoreMode WhenMovedObjectsAreRemoved = SettingDefaults.MovedObjectsWhenRemoved);

    private static IReadOnlyList<Config> Order(bool secondModFirst) => secondModFirst ? [Second, First] : [First, Second];

    /// <summary>The objects a mod's run removes when it runs first.</summary>
    private IReadOnlyList<FormKey> ObjectsOf(Config config) => [_world.NextToLever[config.Plugin], _world.Persistent[config.Plugin]];

    private List<StandardRun> RunChain(IReadOnlyList<Config> order, World world, int workers = Workers)
    {
        var runs = new List<StandardRun>();
        foreach (var config in order) runs.Add(RunStandard(config, world, ChainRunFolderPrefix + runs.Count, runs.LastOrDefault()?.PatchFile, workers));
        return runs;
    }

    /// <param name="earlierPatchFile">The output file of the run before it in the chain; none for the first run.</param>
    private StandardRun RunStandard(Config config, World world, string folderName, string? earlierPatchFile, int workers)
    {
        var folder = CreateRunFolder(folderName);
        var outputPath = Path.Combine(folder, FixturePatcherState.PatchModKey.FileName);
        var state = earlierPatchFile is null
            ? FixturePatcherState.Create(world.LoadOrder, _dataFolder, outputPath)
            : FixturePatcherState.CreateAfterEarlierPatcher(world.LoadOrder, _dataFolder, outputPath, earlierPatchFile);
        var settings = new Settings { DetailedLogAndReports = true, Standard = StandardOf(config, world) };

        var log = PipelineRun.CaptureConsole(
            () => Program.RunPatch(state, () => settings, workers, FixturePatcherState.WritePluginsFile(state, world.BelowPatch, patchEnabled: true)));
        state.PatchMod.WriteToBinary(outputPath);

        Assert.Equal(Banner, log[0]);
        return new StandardRun(
            Mask(log.Skip(1), folder),
            PatchDump.Describe(state.PatchMod),
            state.PatchMod.ModHeader.Description,
            ReadReports(Program.ReportsFolder(state), folder),
            outputPath);
    }

    private BatchRun RunBatch(IReadOnlyList<Config> order, World world, int workers = Workers)
    {
        var folder = CreateRunFolder(BatchRunFolder);
        BatchFiles.WriteGroup(_dataFolder, GroupName, [.. order.Select(config => config.Name)]);
        foreach (var config in order) BatchFiles.WriteConfig(_dataFolder, config.Name, StandardOf(config, world));
        var state = FixturePatcherState.Create(world.LoadOrder, _dataFolder, Path.Combine(folder, FixturePatcherState.PatchModKey.FileName));
        var settings = new Settings { Mode = PatchMode.Batch, DetailedLogAndReports = true, Batch = new BatchSettings { GroupFileName = GroupName } };

        var log = Mask(
            PipelineRun.CaptureConsole(
                () => Program.RunPatch(state, () => settings, workers, FixturePatcherState.WritePluginsFile(state, world.BelowPatch, patchEnabled: true))),
            folder);

        return new BatchRun(
            log,
            RunLogsOfConfigs(log, order),
            PatchDump.Describe(state.PatchMod),
            state.PatchMod.ModHeader.Description,
            [.. order.Select(config => ReadReports(Path.Combine(Program.ReportsFolder(state), config.Name), folder))]);
    }

    private string CreateRunFolder(string name) => Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;

    /// <summary>Each config's section of a batch log, in run order, without the delimiter and the notes the section opens with.</summary>
    private static IReadOnlyList<IReadOnlyList<string>> RunLogsOfConfigs(IReadOnlyList<string> log, IReadOnlyList<Config> order)
    {
        Assert.Equal(Banner, log[0]);
        var starts = log.Select((line, index) => (line, index))
            .Where(entry => entry.line.StartsWith(ConfigDelimiterPrefix, StringComparison.Ordinal))
            .Select(entry => entry.index)
            .Append(log.Count)
            .ToList();
        Assert.Equal(order.Count + 1, starts.Count);
        return
        [
            .. order.Select((config, number) =>
            {
                Assert.StartsWith($"{ConfigDelimiterPrefix}{number + 1} of {order.Count}: {config.Name}.json", log[starts[number]]);
                return (IReadOnlyList<string>)[.. log.Take(starts[number + 1]).Skip(starts[number] + 1).SkipWhile(IsConfigNote)];
            }),
        ];
    }

    private static bool IsConfigNote(string line) => ConfigNotePrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// The run's own folder and a config's report subfolder are the only differences in the paths a run prints, so both are taken
    /// out; the shared Data folder lies outside the run's folder and prints the same for every run.
    /// </summary>
    private static IReadOnlyList<string> Mask(IEnumerable<string> log, string runFolder) =>
        [.. OutputMasks.MaskLog(log, runFolder).Select(WithoutConfigFolder)];

    /// <summary>Each report file as its name line and its lines; none when the run wrote no report folder.</summary>
    private static IReadOnlyList<string> ReadReports(string reportFolder, string runFolder) =>
        Directory.Exists(reportFolder)
            ?
            [
                .. Directory.EnumerateFiles(reportFolder)
                    .Order(StringComparer.Ordinal)
                    .SelectMany(path => (IEnumerable<string>)
                    [
                        ReportNamePrefix + Path.GetFileName(path),
                        .. OutputMasks.MaskReport(File.ReadAllLines(path), runFolder).Select(WithoutConfigFolder),
                    ]),
            ]
            : [];

    private static string WithoutConfigFolder(string line) =>
        new[] { First, Second }.Aggregate(
            line,
            (text, config) => text.Replace($"{Program.ReportsFolderName}/{config.Name}", Program.ReportsFolderName, StringComparison.Ordinal));

    private static IReadOnlyList<FormKey> RemovedRecords(IReadOnlyList<string> patch) =>
        [.. patch.Where(line => line.Contains(RemovedLineMark, StringComparison.Ordinal)).Select(line => FormKey.Factory(line.Split(' ')[0]))];

    private static IReadOnlyList<string> Sorted(IEnumerable<FormKey> records) => [.. records.Select(record => record.ToString()).Order(StringComparer.Ordinal)];

    private static string LineOf(IReadOnlyList<string> patch, FormKey record) =>
        Assert.Single(patch, line => line.StartsWith($"{record} ", StringComparison.Ordinal));

    private static string MarkerMovesLine(int moved) => $"Moved {moved} kept markers out of other mods' objects; 0 left in place.";

    private static string RunSummary(Config config, int alreadyRemoved, int judged, int removed) =>
        $"{RunSummaryPrefix}{config.Plugin}: {ObjectsPerMod}; already removed by earlier patchers in this group: {alreadyRemoved}; "
        + "not judged (winning version from a later plugin, removed by a later plugin, deleted, start disabled, or without a valid position or rotation): 0; "
        + $"judged in this run: {judged}; removed in this run: {removed}.";

    private static StandardSettings StandardOf(Config config, World world) =>
        new Settings
        {
            Standard = new StandardSettings
            {
                CleanedMod = ModKey.FromNameAndExtension(config.Plugin),
                Trust = new TrustSettings { TrustModsLoadingBeforeCleanedMod = false },
                TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = world.RemoveOnChangedTerrain },
                TooClose = new TooCloseRemovalSettings
                {
                    BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = SizeMultiplier },
                    ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = DistanceInUnits },
                },
            },
        }.WithTheCollisionOfTheCleanedModsObjects().WithMovedObjects(world.WhenMovedObjectsAreRemoved).Standard;

    private static World CreateWorld(string dataFolder, bool withPluginBelowPatch, bool withMovedCrate = false)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var first = new FixtureMod(FirstPlugin, baseGame);
        var second = new FixtureMod(SecondPlugin, baseGame);
        var keeper = new FixtureMod(KeeperPlugin, baseGame, first);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(first);
        spaces.AddOverridesTo(second);

        var lever = baseGame.Interior.Place("Lever", baseGame.AddModeledStatic("LeverBase", FixtureMeshes.PadModel), new Vector3(0, -SceneSpacing, 0));
        var crate = first.AddModeledStatic("First_CrateBase", FixtureMeshes.CrateModel);
        var pad = second.AddModeledStatic("Second_PadBase", FixtureMeshes.PadModel);
        var nextToLever = new Dictionary<string, FormKey>
        {
            [FirstPlugin] = WithEnableParent(first.Interior.Place("First_LeverCrate", crate, Vector3.Zero), lever),
            [SecondPlugin] = WithEnableParent(second.Interior.Place("Second_LeverPad", pad, new Vector3(PadCentre, 0, 0)), lever),
        };
        var persistent = new Dictionary<string, FormKey>
        {
            [FirstPlugin] = WithPersistentFlag(first.Interior.Place("First_PersistentCrate", crate, new Vector3(0, SceneSpacing, 0), persistent: true)),
            [SecondPlugin] = WithPersistentFlag(second.Interior.Place("Second_PersistentPad", pad, new Vector3(PadCentre, SceneSpacing, 0), persistent: true)),
        };

        var ground = new Vector3(FixtureExteriorLayout.BuildingX, FixtureExteriorLayout.BuildingY, FixtureSpaces.TerrainHeight);
        second.Exterior.Place("Second_Building", second.AddModeledStatic("Second_BuildingBase", FixtureMeshes.BuildingModel), ground);
        var marker = first.Exterior.Place("First_MarkerInBuilding", first.AddMarkerStatic("First_MarkerBase"), ground + new Vector3(0, 0, MarkerHeight));
        keeper.AddFormList("Keeper_List").Items.Add(new FormLink<ISkyrimMajorRecordGetter>(marker.FormKey));

        IReadOnlyList<ISkyrimMod> belowPatch = [];
        IReadOnlyList<FormKey> removedBecauseOfBelowPatch = [];
        if (withPluginBelowPatch) (belowPatch, removedBecauseOfBelowPatch) = AddPluginBelowPatchScene(dataFolder, baseGame, spaces, first, second);

        ISkyrimMod[] loadOrder = [baseGame.Mod, first.Mod, second.Mod, keeper.Mod];
        FormKey? movedCrate = null;
        if (withMovedCrate)
        {
            var (city, baseGameCrate) = AddMovedCrateScene(baseGame, spaces, first, second);
            loadOrder = [.. loadOrder, city.Mod];
            movedCrate = baseGameCrate;
        }

        return new World(loadOrder, nextToLever, persistent, marker.FormKey, belowPatch, removedBecauseOfBelowPatch, RemoveOnChangedTerrain: withPluginBelowPatch, movedCrate);
    }

    /// <summary>In the interior, a crate of the base game that both mods move, and a wall of a city mod loading after them beside each of the mods' places.</summary>
    /// <returns>The city mod, and the crate.</returns>
    private static (FixtureMod City, FormKey Crate) AddMovedCrateScene(FixtureMod baseGame, FixtureSpaces spaces, FixtureMod first, FixtureMod second)
    {
        var city = new FixtureMod(CityPlugin, baseGame);
        spaces.AddOverridesTo(city);
        var crate = baseGame.Interior.Place("Base_MovedCrate", baseGame.AddModeledStatic("Base_MovedCrateBase", FixtureMeshes.CrateModel), CrateInBaseGame);
        MoveTo(first.Interior.PlaceCopyOf(crate), CrateInFirstMod);
        MoveTo(second.Interior.PlaceCopyOf(crate), CrateInSecondMod);

        var wall = city.AddModeledStatic("City_WallBase", FixtureMeshes.PadModel);
        city.Interior.Place("City_WallBesideFirstModsCrate", wall, CrateInFirstMod + new Vector3(PadCentre, 0, 0));
        city.Interior.Place("City_WallBesideSecondModsCrate", wall, CrateInSecondMod + new Vector3(PadCentre, 0, 0));
        return (city, crate.FormKey);
    }

    private static void MoveTo(PlacedObject copy, Vector3 position) => copy.Placement!.Position = new P3Float(position.X, position.Y, position.Z);

    /// <summary>
    /// Two exterior cells beside the shared one. In the first, a crate of each mod stands on terrain that the plugin below the patch
    /// raises. In the second, a crate of the first mod stands 5 units from a pad that the same plugin places. The plugin is written
    /// to the Data folder, as the final world opens the plugins below the patch from there; it has the base game as its only master.
    /// </summary>
    /// <returns>The plugin, and the crates the runs remove only because of it.</returns>
    private static (IReadOnlyList<ISkyrimMod> BelowPatch, IReadOnlyList<FormKey> Removed) AddPluginBelowPatchScene(
        string dataFolder, FixtureMod baseGame, FixtureSpaces spaces, FixtureMod first, FixtureMod second)
    {
        var below = new FixtureMod(BelowPlugin, baseGame);
        spaces.AddOverridesTo(below);
        var raised = new NeighbourCell(baseGame, RaisedTerrainCell);
        var padded = new NeighbourCell(baseGame, PaddedCell);
        var firstCrate = first.AddModeledStatic("First_TerrainCrateBase", FixtureMeshes.CrateModel);
        var secondCrate = second.AddModeledStatic("Second_TerrainCrateBase", FixtureMeshes.CrateModel);

        var removed = new[]
        {
            raised.CopyTo(first).Place(FirstCrateOnRaisedTerrain, firstCrate, OnGround(RaisedTerrainCell, FirstCrateX)).FormKey,
            raised.CopyTo(second).Place(SecondCrateOnRaisedTerrain, secondCrate, OnGround(RaisedTerrainCell, SecondCrateX)).FormKey,
            padded.CopyTo(first).Place(FirstCrateNextToBelowPad, firstCrate, OnGround(PaddedCell, FirstCrateX)).FormKey,
        };
        raised.CopyTo(below, FixtureSpaces.TerrainHeight / HeightStepUnits + RaisedSteps);
        padded.CopyTo(below).Place("Below_Pad", below.AddModeledStatic("Below_PadBase", FixtureMeshes.PadModel), OnGround(PaddedCell, FirstCrateX + PadCentre));
        below.Mod.WriteToBinary(Path.Combine(dataFolder, below.Key.FileName));
        return ([below.Mod], removed);
    }

    private static Vector3 OnGround(int gridX, float offsetInCell) =>
        new(gridX * ExteriorGrid.CellSize + offsetInCell, SceneSpacing, FixtureSpaces.TerrainHeight);

    /// <summary>An exterior cell beside the shared one: the base game owns it, with the shared cell's flat terrain, and each plugin that changes it holds a copy.</summary>
    private sealed class NeighbourCell
    {
        private readonly FormKey _cell;
        private readonly FormKey _land;
        private readonly int _gridX;

        public NeighbourCell(FixtureMod baseGame, int gridX)
        {
            _gridX = gridX;
            _cell = baseGame.NextKey();
            _land = baseGame.NextKey();
            CopyTo(baseGame, FixtureSpaces.TerrainHeight / HeightStepUnits);
        }

        /// <param name="terrainSteps">The height of the copy's flat terrain in height steps; none for a copy without a land record.</param>
        public FixtureCell CopyTo(FixtureMod mod, float? terrainSteps = null)
        {
            var cell = new Cell(_cell, FixtureMod.Release)
            {
                Grid = new CellGrid { Point = new P2Int(_gridX, 0) },
                Landscape = terrainSteps is { } steps
                    ? new Landscape(_land, FixtureMod.Release) { VertexHeightMap = new LandscapeVertexHeightMap { Offset = steps } }
                    : null,
            };
            mod.Mod.Worldspaces.Single().AddCell(cell);
            return new FixtureCell(mod, cell);
        }
    }

    private static FormKey WithEnableParent(PlacedObject placed, PlacedObject parent)
    {
        placed.EnableParent = FixtureScenery.EnableParentOf(parent);
        return placed.FormKey;
    }

    /// <summary>The removal keeps an object in place by its own Persistent flag; standing in the cell's persistent list is not enough.</summary>
    private static FormKey WithPersistentFlag(PlacedObject placed)
    {
        placed.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent;
        return placed.FormKey;
    }
}
