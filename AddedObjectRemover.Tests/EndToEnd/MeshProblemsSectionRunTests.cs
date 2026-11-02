using System.Diagnostics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Every broken mesh is reported once, in the "meshProblems" section, whichever step read it first. A step's reading is
/// planted after the sections of that step print, so the problem is first met there, as if that step had read the mesh.
/// </summary>
public class MeshProblemsSectionRunTests
{
    private const string MeshSection = "meshProblems";
    private const string Mesh = @"meshes\planted.nif";
    private const string MeshLine = @"  [mesh] not found: meshes\planted.nif";
    private const string ArchiveLine = "  Warning: could not read archive a.bsa: unreadable";

    private static readonly AssetProblem MeshProblem = AssetProblem.NotFound(Mesh);
    private static readonly ArchiveProblem ArchiveProblem = new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "unreadable");

    public static TheoryData<string> StepSections { get; } = new() { "replacements", "tooClose", "touch", "leftovers", "relocations", "write" };

    [Theory]
    [MemberData(nameof(StepSections))]
    public void AMeshProblemPrintsOnceInTheMeshProblemsSectionWhicheverStepMetItFirst(string stepSection)
    {
        var log = RunPlanting(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, stepSection, problems => problems.Add(MeshProblem));

        Assert.Equal([MeshSection], SectionsWithLine(log, line => line == MeshLine));
        Assert.Single(log.Sections.SelectMany(section => section.Lines), MeshLine);
    }

    [Fact]
    public void TheNormalLogShowsNoMeshProblem()
    {
        var log = RunPlanting(SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog, "tooClose", problems => problems.Add(MeshProblem));

        Assert.Empty(SectionsWithLine(log, line => line == MeshLine));
    }

    [Fact]
    public void AnArchiveProblemMetWhileReadingMeshesStaysInTheArchiveWarningsAndTheMeshProblemsHoldOnlyMeshes()
    {
        var log = RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            "write",
            problems =>
            {
                problems.Add(ArchiveProblem);
                problems.Add(MeshProblem);
            });

        Assert.Equal(["archiveWarnings"], SectionsWithLine(log, line => line == ArchiveLine));
        Assert.Equal([MeshSection], SectionsWithLine(log, line => line == MeshLine));
    }

    [Fact]
    public void AMeshProblemRecordedAfterTheMeshProblemsAreTakenIsABugThatEndsTheRun()
    {
        Assert.Throws<UnreachableException>(() => RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            MeshSection,
            problems => problems.Add(MeshProblem)));
    }

    [Fact]
    public void AnArchiveProblemRecordedAfterTheEndOfRunTookThemIsABugThatEndsTheRun()
    {
        Assert.Throws<UnreachableException>(() => RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            MeshSection,
            problems => problems.Add(ArchiveProblem)));
    }

    [Fact]
    public void AnArchiveProblemMetWhileTheEndOfRunReadsMeshesPrintsInTheArchiveWarnings()
    {
        var writeSectionPrinted = false;
        var log = new RecordingLogSink { OnPrint = section => writeSectionPrinted |= section.Id == "write" };

        FixtureRun.Run(
            SettingsVariants.Of(SettingsVariants.TouchWithLeftBehindAndMarkerMoves),
            workers: 1,
            log,
            (dataFolder, release, plugins) => new ProblemOnBoundsAfterFactory(new MeshFilesFactory(dataFolder, release, plugins), () => writeSectionPrinted, ArchiveProblem));

        Assert.Equal(["archiveWarnings"], SectionsWithLine(log, line => line == ArchiveLine));
    }

    private static List<string> SectionsWithLine(RecordingLogSink log, Func<string, bool> isLine) =>
        [.. log.Sections.Where(section => section.Lines.Any(isLine)).Select(section => section.Id)];

    /// <param name="plant">Records problems into the run's problem log; called when the sections of <paramref name="afterSection"/> print.</param>
    private static RecordingLogSink RunPlanting(string variant, string afterSection, Action<AssetProblemLog> plant)
    {
        PlantingMeshFilesFactory? factory = null;
        var log = new RecordingLogSink
        {
            OnPrint = section =>
            {
                if (section.Id == afterSection) plant((AssetProblemLog)factory!.Files!.Problems);
            },
        };

        FixtureRun.Run(
            SettingsVariants.Of(variant),
            workers: 1,
            log,
            (dataFolder, release, plugins) => factory = new PlantingMeshFilesFactory(new MeshFilesFactory(dataFolder, release, plugins)));
        return log;
    }

    /// <summary>Records one archive problem the first time the bounds are asked for once <c>armed</c> says so, as a mesh read at the end of the run would.</summary>
    private sealed class ProblemOnBoundsAfterFactory(IMeshFilesFactory inner, Func<bool> armed, ArchiveProblem problem) : IMeshFilesFactory
    {
        public IMeshFiles Open(ShapeInclusion inclusion) =>
            new ProblemOnBounds(inner.Open(inclusion), armed, problem);
    }

    private sealed class ProblemOnBounds(IMeshFiles inner, Func<bool> armed, ArchiveProblem problem) : IMeshFiles
    {
        private bool _recorded;

        public IMeshBounds Bounds
        {
            get
            {
                if (armed() && !_recorded)
                {
                    _recorded = true;
                    ((AssetProblemLog)inner.Problems).Add(problem);
                }

                return inner.Bounds;
            }
        }

        public IMeshProblems Problems => inner.Problems;

        public int ArchivesIndexed => inner.ArchivesIndexed;

        public void PrepareReading() => inner.PrepareReading();

        public string NormalizeMeshPath(string givenPath) => inner.NormalizeMeshPath(givenPath);

        public MeshTriangles? ReadTriangles(string meshPath) => inner.ReadTriangles(meshPath);

        public TriangleFailureCounts CountTriangleFailures() => inner.CountTriangleFailures();
    }

    /// <summary>Opens the run's mesh reader and keeps it, so a test can record into the run's own problem log.</summary>
    private sealed class PlantingMeshFilesFactory(IMeshFilesFactory inner) : IMeshFilesFactory
    {
        public IMeshFiles? Files { get; private set; }

        public IMeshFiles Open(ShapeInclusion inclusion)
        {
            Files = inner.Open(inclusion);
            return Files;
        }
    }
}
