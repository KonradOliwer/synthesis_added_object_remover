using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Batch mode through the entry, on a small world of its own: the first mod's crate and the second mod's pad stand
/// 5 units apart in one interior, so each mod's object is too close to the other's when its mod is cleaned up.
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
    private const string Banner = "=== Added Object Remover ===";
    private const string DoneLinePrefix = "Done";
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
    public void TheSecondConfigSeesTheObjectTheFirstOneRemovedAsGone()
    {
        WriteGroup(FirstConfig, SecondConfig);
        WriteConfig(FirstConfig, FirstPlugin);
        WriteConfig(SecondConfig, SecondPlugin);

        var run = Run();

        Assert.Equal([Crate], run.RemovedRecords());
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
        WriteFile(Path.Combine("configs", SecondConfig + ".json"), "{\"TargetPlugin\": \"Second.esp\", \"TooClose\": {\"NpcHandling\": \"Sometimes\"}}");

        var run = RunFailing();

        Assert.Contains("Invalid setting in config file", run.Message);
        Assert.Contains(SecondConfig + ".json", run.Message);
        AssertNothingRanOrWritten(run);
    }

    private void AssertNothingRanOrWritten(FailedRun run)
    {
        Assert.Equal([Banner], run.Log);
        Assert.Empty(run.Patch);
        Assert.False(Directory.Exists(Path.Combine(_outputFolder, ReportsFolder)));
    }

    private sealed record FinishedRun(IReadOnlyList<string> Log, IReadOnlyList<string> Patch)
    {
        public IReadOnlyList<FormKey> RemovedRecords() =>
            [.. Patch.Where(line => line.Contains("disabled True")).Select(line => FormKey.Factory(line.Split(' ')[0]))];
    }

    private sealed record FailedRun(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, string Message);

    private FinishedRun Run()
    {
        var state = CreateState();
        var log = PipelineRun.CaptureConsole(() => Program.RunPatch(state, BatchSettings, Workers));
        return new FinishedRun(log, PatchDump.Describe(state.PatchMod));
    }

    private FailedRun RunFailing()
    {
        var state = CreateState();
        BatchStoppedException? stopped = null;
        var log = PipelineRun.CaptureConsole(() => stopped = Assert.Throws<BatchStoppedException>(() => Program.RunPatch(state, BatchSettings, Workers)));
        return new FailedRun(log, PatchDump.Describe(state.PatchMod), stopped!.Message);
    }

    private IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateState() =>
        FixturePatcherState.Create(_world.LoadOrder, _dataFolder, Path.Combine(_outputFolder, FixturePatcherState.PatchModKey.FileName));

    private static IModeSettingsValues BatchSettings() =>
        new Settings { Mode = PatchMode.Batch, DetailedLogAndReports = true, Batch = new BatchSettings { GroupFileName = GroupName } };

    private void WriteGroup(params string[] configs) =>
        WriteFile(Path.Combine("groups", GroupName + ".json"), "[" + string.Join(", ", configs.Select(name => $"\"{name}\"")) + "]");

    private void WriteConfig(string name, string target) =>
        WriteFile(
            Path.Combine("configs", name + ".json"),
            $$"""
            {
              "TargetPlugin": "{{target}}",
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

    private sealed record World(IReadOnlyList<ISkyrimMod> LoadOrder, FormKey Crate, FormKey Pad);

    private static World CreateWorld(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod("Skyrim.esm");
        var first = new FixtureMod(FirstPlugin, baseGame);
        var second = new FixtureMod(SecondPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(first);
        spaces.AddOverridesTo(second);

        var crate = first.Interior.Place("F_Crate", first.AddModeledStatic("F_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);
        var pad = second.Interior.Place("S_Pad", second.AddModeledStatic("S_PadBase", FixtureMeshes.PadModel), new Vector3(PadCentre, 0, 0));
        return new World([baseGame.Mod, first.Mod, second.Mod], crate.FormKey, pad.FormKey);
    }
}
