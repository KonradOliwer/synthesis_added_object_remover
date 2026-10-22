using Mutagen.Bethesda.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Standard mode is a batch of one: a config equal to the Standard settings changes nothing but the lines that open its run.</summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class BatchOfOneRunTests
{
    private const int Workers = 4;
    private const string ConfigName = "same_as_standard";
    private const string GroupName = "my_group";
    private const string Banner = "=== Added Object Remover ===";
    private const string DelimiterPrefix = "=== Config 1 of 1: " + ConfigName + ".json";
    private const string NoteLineStart = "  ";

    [Fact]
    public void ABatchOfOneConfigEqualToTheStandardSettingsGivesTheStandardLogPlusTheConfigLinesAndTheSamePatch()
    {
        var settings = SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves);

        var standard = Run(settings, batch: false);
        var batch = Run(settings, batch: true);

        Assert.Equal(standard.Patch, batch.Patch);
        Assert.Equal(Banner, batch.Log[0]);
        Assert.StartsWith(DelimiterPrefix, batch.Log[1]);
        var extraLines = batch.Log.Count - standard.Log.Count;
        Assert.All(batch.Log.Skip(2).Take(extraLines - 1), line => Assert.True(line.StartsWith("Set in ") || line.StartsWith(NoteLineStart), line));
        Assert.Equal(standard.Log.Skip(1), batch.Log.Skip(1 + extraLines));
    }

    private static RunOutput Run(Settings settings, bool batch)
    {
        var root = Directory.CreateTempSubdirectory("aor-batch-of-one-");
        try
        {
            var dataFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Data")).FullName;
            var outputFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "Output")).FullName;
            var state = FixturePatcherState.Create(
                FixtureWorld.Create(dataFolder).LoadOrder, dataFolder, Path.Combine(outputFolder, FixturePatcherState.PatchModKey.FileName));
            var modeSettings = batch ? BatchSettingsWithConfig(settings, dataFolder) : settings;

            var log = PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => modeSettings, Workers, FixturePatcherState.WritePluginsFile(state)));

            return new RunOutput(MaskedLog(log, root.FullName), PatchDump.Describe(state.PatchMod), [], log);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>The config's report subfolder is the one difference in the paths the log shows, so it is taken out of them.</summary>
    private static IReadOnlyList<string> MaskedLog(IReadOnlyList<string> log, string root) =>
        [.. OutputMasks.MaskLog(log, root).Select(line => line.Replace($"{Program.ReportsFolderName}/{ConfigName}", Program.ReportsFolderName))];

    private static Settings BatchSettingsWithConfig(Settings settings, string dataFolder)
    {
        WriteFile(dataFolder, Path.Combine("groups", GroupName + ".json"), $"[\"{ConfigName}\"]");
        WriteFile(dataFolder, Path.Combine("configs", ConfigName + ".json"), JsonConvert.SerializeObject(settings.Standard, ConfigJson()));
        return new Settings
        {
            Mode = PatchMode.Batch,
            DetailedLogAndReports = settings.DetailedLogAndReports,
            Batch = new BatchSettings { GroupFileName = GroupName },
        };
    }

    private static JsonSerializerSettings ConfigJson()
    {
        var json = new JsonSerializerSettings();
        json.Converters.Add(new StringEnumConverter());
        json.AddMutagenConverters();
        return json;
    }

    private static void WriteFile(string dataFolder, string relativePath, string content)
    {
        var path = Path.Combine(dataFolder, "synthesis_added_object_remover", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
