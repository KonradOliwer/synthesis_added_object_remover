using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Whole runs through the entry over <see cref="TexturesAndWaterWorld"/> with its second mod to clean up. The first run cleans up the
/// cleaned mod and gives cell (0, 0) back the untrusted earlier plugin's road and high water. The second run cleans up the second mod,
/// whose tree stands where its changed-terrain check reaches that water, and does not trust the earlier plugin. It reads the water
/// and the road as the world without the first cleaned mod, so it keeps the tree, in one Batch run and in a chain of two copies
/// alike. Without the restored parts file, the chain's second copy sees the water as another patcher's change and removes the tree.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class RestoredValuesInLaterRunsTests : IDisposable
{
    private const int Workers = 4;
    private const string GroupName = "pond";
    private const string SeedFolder = "Seed Mod";

    private static readonly Config Cleaned = new("cleaned", FixtureWorld.TargetName);
    private static readonly Config Forest = new("forest", TexturesAndWaterWorld.SecondCleanedPlugin);
    private static readonly Config EarlierCleaned = new("earlier", TexturesAndWaterWorld.EarlierPlugin);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-restored-values-");
    private string _dataFolder;
    private IReadOnlyList<ISkyrimMod> _loadOrder;
    private FormKey _tree;

    public RestoredValuesInLaterRunsTests() => (_dataFolder, _loadOrder, _tree) = CreateWorld("Data", withRaisedGroundAndMovedBoulder: false);

    private (string DataFolder, IReadOnlyList<ISkyrimMod> LoadOrder, FormKey Tree) CreateWorld(string folder, bool withRaisedGroundAndMovedBoulder)
    {
        var dataFolder = CreateFolder(folder);
        var loadOrder = TexturesAndWaterWorld.Create(dataFolder, earlierFromTheBaseGame: false, withSecondCleanedMod: true, withRaisedGroundAndMovedBoulder);
        var tree = loadOrder[^1].EnumerateMajorRecords<IPlacedObjectGetter>().Single(placed => placed.EditorID == TexturesAndWaterWorld.SecondCleanedModsTree).FormKey;
        return (dataFolder, loadOrder, tree);
    }

    public void Dispose() => _root.Delete(recursive: true);

    /// <param name="Name">The config file's name, without .json.</param>
    private sealed record Config(string Name, string Plugin);

    /// <param name="Placed">The patch's placed records, one line each.</param>
    /// <param name="Areas">The patch's cells, land and other area records, one line each.</param>
    /// <param name="WaterHeights">The water height of each of the patch's exterior cells with water, by grid.</param>
    private sealed record Patch(IReadOnlyList<string> Placed, IReadOnlyList<string> Areas, IReadOnlyDictionary<(int X, int Y), float> WaterHeights);

    [Fact]
    public void ABatchRunKeepsTheTreeByTheWaterTheFirstRunGaveBack()
    {
        var batch = RunBatch(Workers);

        Assert.DoesNotContain(batch.Placed, IsTreeLine);
        Assert.Equal(TexturesAndWaterWorld.WaterAboveTheGround, batch.WaterHeights[(0, 0)]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Workers)]
    public void AChainOfTwoCopiesKeepsTheTreeAndWritesThePatchOfOneBatchRun(int workers)
    {
        var first = RunCopy(Cleaned, FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("first")), PatchIn(CreateFolder(SeedFolder))), workers);
        var second = RunNextCopy(Forest, first, workers);

        var batch = RunBatch(Workers);

        Assert.DoesNotContain(second.Patch.Placed, IsTreeLine);
        Assert.Equal(batch.Placed, second.Patch.Placed);
        Assert.Equal(batch.Areas, second.Patch.Areas);
        Assert.Equal(batch.WaterHeights, second.Patch.WaterHeights);
    }

    /// <summary>
    /// The first copy also gives back the earlier plugin's raised ground and its boulder's position, so the parts it hands on hold
    /// heights and a placed record's position; the chain still writes the patch of one Batch run.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(Workers)]
    public void AChainWritesThePatchOfOneBatchRunWhenTheFirstCopyGaveBackHeightsAndAPosition(int workers)
    {
        (_dataFolder, _loadOrder, _tree) = CreateWorld("Raised Data", withRaisedGroundAndMovedBoulder: true);
        var first = RunCopy(Cleaned, FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("first")), PatchIn(CreateFolder(SeedFolder))), workers);
        var second = RunNextCopy(Forest, first, workers);

        var batch = RunBatch(Workers);

        var handedOn = RestoredPartsText.Read(File.ReadAllText(RestoredPartsFile.PathBeside(first.PatchFile)), first.PatchFile).Parts;
        Assert.Contains(handedOn, part => part.Part is RestoredPartName.LandHeights);
        Assert.Contains(handedOn, part => part.Part == new RestoredPartName.Placed(PlacedParts.Position));
        Assert.Equal(batch.Placed, second.Patch.Placed);
        Assert.Equal(batch.Areas, second.Patch.Areas);
    }

    /// <summary>
    /// The earlier plugin also moves a base game barrel from under its pond, in cell (0, 0), to beside the second mod's tree, and is
    /// cleaned up after the first run gave the pond back its water: the barrel is too close to the tree, so its earlier version is
    /// checked at its old place in the world without the earlier plugin. The water there is the earlier plugin's own, put back by the
    /// first run, so it is no part of that world: the barrel stands under changed water there and is removed, not restored. Without
    /// the first run the pond holds the cleaned mod's low water, which that world accepts, and the barrel is restored.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AValueAnEarlierRunGaveBackFromTheModALaterRunCleansUpIsNoPartOfTheWorldWithoutThatMod(bool firstRunGaveBackTheWater)
    {
        var barrel = CreateBarrelWorld();

        var batch = RunBatch(Workers, firstRunGaveBackTheWater ? new[] { Cleaned, EarlierCleaned } : new[] { EarlierCleaned });

        var barrelLine = Assert.Single(batch.Placed, line => line.StartsWith($"{barrel} ", StringComparison.Ordinal));
        Assert.Contains(firstRunGaveBackTheWater ? "disabled True" : "disabled False", barrelLine);
    }

    /// <summary>As above, as a chain of two copies: the chain writes the patch of one Batch run.</summary>
    [Fact]
    public void AChainRemovesTheBarrelAsOneBatchRunDoes()
    {
        CreateBarrelWorld();
        var first = RunCopy(Cleaned, FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("first")), PatchIn(CreateFolder(SeedFolder))), Workers);
        var second = RunNextCopy(EarlierCleaned, first, Workers);

        var batch = RunBatch(Workers, [Cleaned, EarlierCleaned]);

        Assert.Equal(batch.Placed, second.Patch.Placed);
        Assert.Equal(batch.Areas, second.Patch.Areas);
    }

    /// <returns>The barrel's key.</returns>
    private FormKey CreateBarrelWorld()
    {
        _dataFolder = CreateFolder("Barrel Data");
        _loadOrder = TexturesAndWaterWorld.Create(_dataFolder, earlierFromTheBaseGame: false, withSecondCleanedMod: true, withBarrelTheEarlierPluginMoves: true);
        return _loadOrder[0].EnumerateMajorRecords<IPlacedObjectGetter>().Single(placed => placed.EditorID == TexturesAndWaterWorld.Barrel).FormKey;
    }

    [Fact]
    public void WithoutTheRestoredPartsFileTheNextCopyRemovesTheTree()
    {
        var first = RunCopy(Cleaned, FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("first")), PatchIn(CreateFolder(SeedFolder))), Workers);
        File.Delete(RestoredPartsFile.PathBeside(first.PatchFile));

        var second = RunNextCopy(Forest, first, Workers);

        Assert.Contains(second.Patch.Placed, IsTreeLine);
    }

    private bool IsTreeLine(string line) => line.StartsWith($"{_tree} ", StringComparison.Ordinal);

    /// <param name="PatchFile">The output file the copy wrote, which the next copy of the group is handed.</param>
    private sealed record CopyRun(Patch Patch, string PatchFile);

    private CopyRun RunNextCopy(Config config, CopyRun before, int workers) =>
        RunCopy(config, FixturePatcherState.CreateAfterEarlierPatcher(_loadOrder, _dataFolder, PatchIn(CreateFolder("next")), before.PatchFile, namesSource: true), workers);

    private static CopyRun RunCopy(Config config, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, int workers)
    {
        var settings = SettingsOf(config);
        PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => settings, workers, FixturePatcherState.WritePluginsFile(state)));
        state.PatchMod.WriteToBinary(state.OutputPath.Path);
        return new CopyRun(PatchOf(state.PatchMod), state.OutputPath.Path);
    }

    private Patch RunBatch(int workers) => RunBatch(workers, [Cleaned, Forest]);

    private Patch RunBatch(int workers, Config[] configs)
    {
        BatchFiles.WriteGroup(_dataFolder, GroupName, [.. configs.Select(config => config.Name)]);
        foreach (var config in configs) BatchFiles.WriteConfig(_dataFolder, config.Name, SettingsOf(config).Standard);
        var state = FixturePatcherState.Create(_loadOrder, _dataFolder, PatchIn(CreateFolder("batch")), PatchIn(CreateFolder(SeedFolder)));
        var settings = new Settings { Mode = PatchMode.Batch, Batch = new BatchSettings { GroupFileName = GroupName } };

        PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => settings, workers, FixturePatcherState.WritePluginsFile(state)));

        return PatchOf(state.PatchMod);
    }

    private static Patch PatchOf(ISkyrimModGetter patch) =>
        new(
            PatchDump.Describe(patch),
            AreaPatchDump.Describe(patch),
            patch.EnumerateMajorRecords<ICellGetter>()
                .Where(cell => cell.Grid is not null && cell.Flags.HasFlag(Cell.Flag.HasWater))
                .ToDictionary(cell => (cell.Grid!.Point.X, cell.Grid.Point.Y), cell => cell.WaterHeight ?? float.NaN));

    /// <remarks>The earlier plugin's run restores nothing where it was removed, so the barrel can only go back by its old-place check.</remarks>
    private static Settings SettingsOf(Config config)
    {
        var settings = TexturesAndWaterRunTests.Settings();
        settings.Standard.CleanedMod = ModKey.FromNameAndExtension(config.Plugin);
        if (config == EarlierCleaned) RestoreWhereRemovedRunTests.SwitchEverythingOff(settings.Standard.RestoreWhereRemoved);
        return settings;
    }

    private string CreateFolder(string name) => Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;

    private static string PatchIn(string folder) => Path.Combine(folder, FixturePatcherState.PatchModKey.FileName);
}
