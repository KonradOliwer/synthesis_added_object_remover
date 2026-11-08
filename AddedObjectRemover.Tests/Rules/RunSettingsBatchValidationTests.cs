using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static RunSettingsValidationScene;

public class RunSettingsBatchValidationTests
{
    [Fact]
    public void StandardReportsGoToTheReportsFolderItself() =>
        Assert.Equal(ReportsFolder, BuildReady(SettingsFor(), detailedLogAndReports: true).Settings.Reports?.Folder);

    [Fact]
    public void AConfigFilesReportsGoToASubfolderNamedAfterTheConfig()
    {
        var read = new ConfigsRead.Ready([new ConfiguredSettings(ConfigSource("town_walls", 1, 1), SettingsFor())]);
        var batch = Assert.IsType<BatchRequest.Ready>(BatchRequests.BuildAll(read, new PluginNameParsingPlugin(), Facts(), detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, 1));
        var ready = Assert.IsType<RunRequest.Ready>(Assert.Single(batch.Requests));

        Assert.Equal(Path.Combine(ReportsFolder, "town_walls"), ready.Settings.Reports?.Folder);
    }

    [Fact]
    public void TheSourceReachesTheRequest()
    {
        var source = ConfigSource("town_walls", 1, 2);

        var request = Assert.Single(BuildBatch(source, SettingsFor()).Requests);

        Assert.Same(source, request.Source);
        Assert.IsType<SettingsSource.Standard>(BuildReady(SettingsFor()).Source);
    }

    [Fact]
    public void AReadFailurePassesThroughUnchanged()
    {
        var failed = BuildAll(new ConfigsRead.Failed("Group file x is missing."));

        Assert.Equal("Group file x is missing.", Assert.IsType<BatchRequest.Failed>(failed).Message);
    }

    [Fact]
    public void AConfigFileWithoutATargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), new StandardSettings());

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")} sets no mod to clean up (CleanedMod). Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotInThePluginListFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Missing.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Missing.esp is not in your plugin list. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithADisabledTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), DisabledTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is disabled. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetNotLoadedBySynthesisFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), NotLoadedTargetFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp is enabled in your plugin list but Synthesis did not load it for this group. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithATargetLoadingAfterThePatchFailsTheBatchNamingTheFileAndBothPositions()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor(), TargetAfterPatchFacts);

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Target.esp loads after the patch Patch.esp (position 3 > 1): move Patch.esp below it. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AConfigFileWithThePatchPluginAsTargetFailsTheBatchNamingTheFile()
    {
        var failed = FailedBatch(ConfigSource("town_walls", 1, 1), SettingsFor("Patch.esp"));

        Assert.Equal(
            $"Config file {FilePathOf("town_walls")}: Patch.esp is the patch this patcher writes. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void TheFirstConfigFileThatStopsIsTheOneReportedInGroupOrder()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 3), SettingsFor()),
            new ConfiguredSettings(ConfigSource("second", 2, 3), new StandardSettings()),
            new ConfiguredSettings(ConfigSource("third", 3, 3), SettingsFor("Missing.esp")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.StartsWith($"Config file {FilePathOf("second")} sets no mod", failed.Message);
    }

    [Fact]
    public void TwoConfigFilesWithTheSameTargetFailTheBatchNamingBothFiles()
    {
        var read = new ConfigsRead.Ready(
        [
            new ConfiguredSettings(ConfigSource("first", 1, 2), SettingsFor("Target.esp")),
            new ConfiguredSettings(ConfigSource("second", 2, 2), SettingsFor("target.ESP")),
        ]);

        var failed = Assert.IsType<BatchRequest.Failed>(BuildAll(read));

        Assert.Equal(
            $"Config files {FilePathOf("first")} and {FilePathOf("second")} both clean up Target.esp; each mod may be cleaned up by one config only. Nothing was run and no patch was written.",
            failed.Message);
    }

    [Fact]
    public void AStandardRequestThatStopsStaysAStopRequest()
    {
        var read = new ConfigsRead.Ready([StandardConfig.Of(new StandardSettings())]);

        var batch = Assert.IsType<BatchRequest.Ready>(BuildAll(read));

        Assert.Equal(new StopReason(StopKind.NoTargetSet, null), Assert.IsType<RunRequest.Stop>(Assert.Single(batch.Requests)).Reason);
    }

    private static string FilePathOf(string name) => Path.Combine("configs", name + ".json");

    private static SettingsSource.ConfigFile ConfigSource(string name, int number, int count) =>
        new(name, name + ".json", FilePathOf(name), number, count, new ConfigFileNotes([], [], []));

    private static BatchRequest.Ready BuildBatch(SettingsSource source, StandardSettings settings) =>
        Assert.IsType<BatchRequest.Ready>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)])));

    private static BatchRequest.Failed FailedBatch(SettingsSource source, StandardSettings settings, LoadOrderPlugins? facts = null) =>
        Assert.IsType<BatchRequest.Failed>(BuildAll(new ConfigsRead.Ready([new ConfiguredSettings(source, settings)]), facts));
}
