using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Target objects that all fail the same way in the too-close check give one line in the normal log, the full list in the detailed log,
/// and are not removed; the run goes on and the patch is written.
/// </summary>
public partial class ObjectFailureRunTests
{
    private const string TooCloseLinePrefix = "  Unexpected error while checking objects for being too close (InvalidOperationException: crate triangles broke): ";
    private const string DetailedListIndent = "    ";

    [GeneratedRegex(@"^(?<count>[\d,]+) objects?, e\.g\. .+\. They were not removed\.$")]
    private static partial Regex TooCloseGroup();

    private static Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> BrokenCrate =>
        (folder, release, plugins) => new BrokenTrianglesFactory(new MeshFilesFactory(folder, release, plugins), FixtureMeshes.CrateModel);

    private static (RunOutcome Outcome, ImmutableArray<string> ErrorLines) Run(string variant, int workers, Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> openMeshFiles)
    {
        var log = new RecordingLogSink();
        var done = (RunResult.Done)FixtureRun.Run(SettingsVariants.Of(variant), workers, log, openMeshFiles);
        return (done.Outcome, [.. log.Sections.Where(section => section.Id == "unexpectedErrors").SelectMany(section => section.Lines)]);
    }

    private static Func<string, GameRelease, IReadOnlyList<ModKey>, IMeshFilesFactory> Healthy =>
        (folder, release, plugins) => new MeshFilesFactory(folder, release, plugins);

    [Fact]
    public void ObjectsFailingTheSameWayAreOneLineAndTheyAreNotRemoved()
    {
        var healthy = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog, workers: 1, Healthy).Outcome;

        var (outcome, errorLines) = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog, workers: 1, BrokenCrate);

        var groupLine = Assert.Single(errorLines, line => line.StartsWith(TooCloseLinePrefix, StringComparison.Ordinal));
        var group = TooCloseGroup().Match(groupLine[TooCloseLinePrefix.Length..]);
        Assert.True(group.Success, groupLine);
        Assert.True(int.Parse(group.Groups["count"].Value.Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture) > 1);
        Assert.NotEmpty(outcome.Removals);
        Assert.True(outcome.Removals.Select(removal => removal.TargetIndex).ToHashSet().IsSubsetOf(healthy.Removals.Select(removal => removal.TargetIndex)));
        Assert.True(outcome.Removals.Length < healthy.Removals.Length);
        Assert.Equal(outcome.Removals.Length, outcome.Written.Removed);
    }

    [Fact]
    public void TheDetailedLogListsEveryFailedObjectAfterTheLine()
    {
        var (_, errorLines) = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, workers: 1, BrokenCrate);

        var groupIndex = errorLines.ToList().FindIndex(line => line.StartsWith(TooCloseLinePrefix, StringComparison.Ordinal));
        var count = int.Parse(
            TooCloseGroup().Match(errorLines[groupIndex][TooCloseLinePrefix.Length..]).Groups["count"].Value.Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture);
        var listed = errorLines.Skip(groupIndex + 1).TakeWhile(line => line.StartsWith(DetailedListIndent, StringComparison.Ordinal)).ToList();
        Assert.True(count > UnexpectedErrorLines.ExampleCount);
        Assert.Equal(count, listed.Count);
        Assert.Equal(count, listed.Distinct().Count());
    }

    [Fact]
    public void TheErrorLinesDoNotDependOnTheThreadCount()
    {
        var single = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, workers: 1, BrokenCrate);
        var several = Run(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, workers: 4, BrokenCrate);

        Assert.Equal(single.ErrorLines.ToList(), several.ErrorLines.ToList());
        Assert.Equal(single.Outcome.Removals.Select(removal => removal.TargetIndex), several.Outcome.Removals.Select(removal => removal.TargetIndex));
    }

    private sealed class BrokenTrianglesFactory(IMeshFilesFactory inner, string brokenModel) : IMeshFilesFactory
    {
        public IMeshFiles Open(ShapeInclusion inclusion)
        {
            var files = inner.Open(inclusion);
            return new BrokenTriangles(files, files.NormalizeMeshPath(brokenModel));
        }
    }

    /// <summary>The mesh reader of a run, except that reading the broken mesh's triangles throws.</summary>
    private sealed class BrokenTriangles(IMeshFiles inner, string brokenMesh) : IMeshFiles
    {
        public IMeshBounds Bounds => inner.Bounds;

        public IMeshProblems Problems => inner.Problems;

        public int ArchivesIndexed => inner.ArchivesIndexed;

        public void PrepareReading() => inner.PrepareReading();

        public string NormalizeMeshPath(string givenPath) => inner.NormalizeMeshPath(givenPath);

        public MeshTriangles? ReadTriangles(string meshPath) =>
            string.Equals(meshPath, brokenMesh, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("crate triangles broke")
                : inner.ReadTriangles(meshPath);
    }
}
