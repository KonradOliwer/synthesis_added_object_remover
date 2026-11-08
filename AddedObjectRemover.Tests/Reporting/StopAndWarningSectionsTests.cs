using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

using static LogSectionScene;

/// <summary>The stop and warning log sections give the text checked in as expected-output files or asserted here.</summary>
[ExpectedOutputTest]
public sealed class StopAndWarningSectionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Missing.esp")]
    public void Stop_GivesTheTextOfEveryUnusableTarget(string? target)
    {
        var settings = new StandardSettings { CleanedMod = target == null ? ModKey.Null : ModKey.FromNameAndExtension(target) };
        var stop = Assert.IsType<RunRequest.Stop>(Build(settings, Facts()));

        AssertExpectedOutput(LogSections.BuildStopSection(stop.Reason, stop.Source).Lines.ToArray(), "Stop_GivesTheTextOfEveryUnusableTarget", $"{target ?? "null"}");
    }

    [Fact]
    public void Stop_SaysThereIsNothingToCheck()
    {
        Assert.Equal(
            ["Nothing to check. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.NoTargetObjects, null), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheBatchGoesOnWhenAConfigHasNothingToCheck()
    {
        var config = new SettingsSource.ConfigFile("town", "town.json", "town.json", 1, 2, new ConfigFileNotes([], [], []));

        Assert.Equal(
            ["Nothing to check for this config. No changes made for it; the batch goes on."],
            LogSections.BuildStopSection(new StopReason(StopKind.NoTargetObjects, null), config).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheCleanedModIsDisabled()
    {
        Assert.Equal(
            ["Target.esp is disabled. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.CleanedModDisabled, "Target.esp"), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysSynthesisDidNotLoadTheEnabledCleanedMod()
    {
        Assert.Equal(
            ["Target.esp is enabled in your plugin list but Synthesis did not load it for this group. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.CleanedModNotLoadedBySynthesis, "Target.esp"), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheCleanedModLoadsAfterThePatchWithBothPositions()
    {
        var reason = new StopReason(StopKind.CleanedModLoadsAfterPatch, "Target.esp", new PatchPlacement("Patch.esp", "Target.esp", 300, 250));

        Assert.Equal(
            ["Target.esp loads after the patch Patch.esp (position 300 > 250): move Patch.esp below it. No changes made."],
            LogSections.BuildStopSection(reason, new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheTargetIsThePatch()
    {
        Assert.Equal(
            ["Patch.esp is the patch this patcher writes. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.TargetIsThePatch, "Patch.esp"), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem()
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = (NpcHandling)99;
        settings.TooClose.BySize.SizeMultiplier = 9f;
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings { SurroundingObjects = new SurroundingObjectsSettings { SearchRadius = 1f } };
        var ready = Assert.IsType<RunRequest.Ready>(Build(settings, Facts()));

        var lines = LogSections.BuildWarningsSection(ready.Warnings).Lines.ToArray();

        Assert.Equal(ready.Warnings.Select(warning => warning.Message), lines);
        AssertExpectedOutput(lines, "Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem");
    }

    [Fact]
    public void ReportFolderWarning_WordsTheFailedDeletion()
    {
        var section = LogSections.BuildReportFolderWarningSection(
            new ReportFileProblem(ReportFileAction.DeletingEarlierFiles, "/r", FileName: null, "denied"));

        Assert.Equal(["  Warning: deleting earlier diagnostics files in /r failed: denied"], section.Lines.ToArray());
    }

    [Fact]
    public void ReportFileWarnings_WordEachFailedWriteInOrder()
    {
        var section = LogSections.BuildReportFileWarningsSection(
        [
            new ReportFileProblem(ReportFileAction.WritingFile, "/r", "a.csv", "denied"),
            new ReportFileProblem(ReportFileAction.WritingFile, "/r", "b.csv", "in use"),
        ]);

        Assert.Equal(
            ["  Warning: writing a.csv in /r failed: denied", "  Warning: writing b.csv in /r failed: in use"],
            section.Lines.ToArray());
    }

    [Fact]
    public void ArchiveWarnings_ShowOneLinePerProblem()
    {
        ArchiveProblem[] problems =
        [
            new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "unreadable"),
            new(ArchiveProblemKind.DataFolderUnlistable, "/data", "denied"),
        ];

        AssertExpectedOutput(LogSections.BuildArchiveWarningsSection(problems).Lines.ToArray(), "ArchiveWarnings_ShowOneLinePerProblem");
        Assert.Empty(LogSections.BuildArchiveWarningsSection([]).Lines);
    }
}
