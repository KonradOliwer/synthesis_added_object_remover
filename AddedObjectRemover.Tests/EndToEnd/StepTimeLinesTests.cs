using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// The detailed log gives each step its time once: on a step-time line, unless a line of the step's own section carries it. The
/// normal log has no step-time line. The saved expected-output files drop these lines, so the run's lines are read unmasked here.
/// </summary>
public partial class StepTimeLinesTests
{
    private const string StepTimePrefix = "Step time: ";

    /// <summary>The steps whose sections have no line that carries the step's time.</summary>
    private static readonly string[] StepsWithStepTimeLine =
    [
        "Preparing to read meshes",
        "Identifying the mods",
        "Reading the links from records other than placed objects",
        "Finding the objects to keep",
        "Also remove",
        "Moved kept markers",
    ];

    /// <summary>The sections whose own line carries the step's time when it is shown; otherwise they print the step-time line.</summary>
    private static readonly string[] SectionsTimedEitherWay = ["held-up", "fit", "left-without-support-step"];

    [GeneratedRegex(@"^Step time: (?<step>.+) in \d+\.\d+s\.$")]
    private static partial Regex StepTimeLine();

    [Fact]
    public void TheDetailedLogPrintsAStepTimeLineForEachStepWhoseSectionCarriesNoTime()
    {
        var log = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMoves);

        var stepTimes = log.Sections
            .Where(section => !SectionsTimedEitherWay.Contains(section.Id))
            .SelectMany(section => section.Lines)
            .Where(line => line.StartsWith(StepTimePrefix, StringComparison.Ordinal))
            .Select(line => StepTimeLine().Match(line).Groups["step"].Value);
        Assert.Equal(StepsWithStepTimeLine, stepTimes);
    }

    [Fact]
    public void TheDetailedLogTimesEachStepWhoseSectionMayCarryItsTimeOnce()
    {
        var log = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMoves);

        var sections = log.Sections.Where(section => SectionsTimedEitherWay.Contains(section.Id)).ToList();
        Assert.NotEmpty(sections);
        Assert.All(sections, section => Assert.Single(section.Lines, line => OutputMasks.Duration().IsMatch(line)));
    }

    [Fact]
    public void TheNormalLogPrintsNoStepTimeLine()
    {
        var log = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog);

        Assert.DoesNotContain(log.Sections.SelectMany(section => section.Lines), line => line.StartsWith(StepTimePrefix, StringComparison.Ordinal));
    }

    private static RecordingLogSink Run(string variant)
    {
        var log = new RecordingLogSink();
        FixtureRun.Run(SettingsVariants.Of(variant), workers: 1, log, OpenMeshFiles);
        return log;
    }

    private static IMeshFilesFactory OpenMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);
}
