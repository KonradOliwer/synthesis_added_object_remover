using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>A mesh-loader start-up failure is an unexpected error: it prints before the first mesh is read, and the run carries on.</summary>
public class LoaderWarmUpFailureTests
{
    private const string LoaderFailure = "InvalidOperationException: warm-up broke";

    [Fact]
    public void AFailedWarmUpIsReportedOnceBeforeTheTargetPluginLineAndTheRunCompletes()
    {
        var log = new RecordingLogSink();

        var result = FixtureRun.Run(SettingsVariants.Of(SettingsVariants.Support), workers: 1, log, FailingLoader);

        Assert.IsType<RunResult.Done>(result);
        var configIndex = log.Sections.FindIndex(section => section.Lines.Any(line => line.StartsWith("Target plugin:", StringComparison.Ordinal)));
        var errorIndexes = log.Sections
            .Select((section, index) => (section, index))
            .Where(entry => entry.section.Lines.Any(line => line.Contains(LoaderFailure, StringComparison.Ordinal)))
            .Select(entry => entry.index)
            .ToList();
        var errorSection = log.Sections[Assert.Single(errorIndexes)];
        Assert.Equal("unexpectedErrors", errorSection.Id);
        Assert.True(errorIndexes[0] < configIndex);
        Assert.Equal(
            $"  Unexpected error in the NIF loader start-up ({LoaderFailure}). Meshes are read one at a time, which is slower; the results are not affected.",
            Assert.Single(errorSection.Lines));
    }

    [Fact]
    public void AFailedWarmUpLeavesTheResultsOfAWorkingOneUnchanged()
    {
        var withFailure = FixtureRun.Run(SettingsVariants.Of(SettingsVariants.Support), workers: 1, new RecordingLogSink(), FailingLoader);
        var without = FixtureRun.Run(SettingsVariants.Of(SettingsVariants.Support), workers: 1, new RecordingLogSink(), (folder, release, plugins) => new MeshFilesFactory(folder, release, plugins));

        Assert.Equal(
            ((RunResult.Done)without).Outcome.Removals.Select(removal => removal.TargetIndex),
            ((RunResult.Done)withFailure).Outcome.Removals.Select(removal => removal.TargetIndex));
    }

    private static IMeshFilesFactory FailingLoader(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new FailingLoaderMeshFilesFactory(dataFolder, release, listedPlugins);

    private sealed class FailingLoaderMeshFilesFactory(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) : IMeshFilesFactory
    {
        public IMeshFiles Open(ShapeInclusion inclusion)
        {
            var problems = new AssetProblemLog();
            return new MeshFiles(
                new MeshFileSource(dataFolder, release, listedPlugins, problems), new NifGeometryReader(() => LoaderFailure), problems, inclusion);
        }
    }
}
