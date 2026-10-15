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
    private const string MeshErrorLinePrefix = "  Unexpected error while reading mesh triangles";
    private const string ArchiveLine = "  Warning: could not read archive a.bsa: unreadable";

    private static readonly AssetProblem MeshProblem = AssetProblem.NotFound(Mesh);
    private static readonly ArchiveProblem ArchiveProblem = new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "unreadable");

    public static TheoryData<string> StepSections { get; } = new() { "replacements", "tooClose", "touch", "leftovers", "relocations", "write" };

    [Theory]
    [MemberData(nameof(StepSections))]
    public void AMeshProblemPrintsOnceInTheMeshProblemsSectionWhicheverStepMetItFirst(string stepSection)
    {
        var log = RunPlanting(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, stepSection, (problems, _) => problems.Add(MeshProblem));

        Assert.Equal([MeshSection], SectionsWithLine(log, line => line == MeshLine));
        Assert.Single(log.Sections.SelectMany(section => section.Lines), MeshLine);
    }

    [Theory]
    [MemberData(nameof(StepSections))]
    public void AMeshWhoseReadingThrowsGivesOneGroupedLineInTheMeshProblemsSectionAndNoneInTheErrorSections(string stepSection)
    {
        var log = RunPlanting(SettingsVariants.TouchWithLeftBehindAndMarkerMoves, stepSection, PlantMeshError);

        Assert.Equal([MeshSection], SectionsWithLine(log, line => line.StartsWith(MeshErrorLinePrefix, StringComparison.Ordinal)));
        Assert.Single(log.Sections.SelectMany(section => section.Lines), line => line.StartsWith(MeshErrorLinePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void TheNormalLogShowsTheMeshErrorButNotTheMeshProblem()
    {
        var log = RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMovesNormalLog,
            "tooClose",
            (problems, errors) =>
            {
                problems.Add(MeshProblem);
                PlantMeshError(problems, errors);
            });

        Assert.Empty(SectionsWithLine(log, line => line == MeshLine));
        Assert.Equal([MeshSection], SectionsWithLine(log, line => line.StartsWith(MeshErrorLinePrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void AnArchiveProblemMetWhileReadingMeshesStaysInTheArchiveWarningsAndTheMeshProblemsHoldOnlyMeshes()
    {
        var log = RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            "write",
            (problems, _) =>
            {
                problems.Add(ArchiveProblem);
                problems.Add(MeshProblem);
            });

        Assert.Equal(["archiveWarnings"], SectionsWithLine(log, line => line == ArchiveLine));
        Assert.Equal([MeshSection], SectionsWithLine(log, line => line == MeshLine));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMeshProblemOrMeshErrorRecordedAfterTheMeshProblemsAreTakenIsABugThatEndsTheRun(bool recordAnError)
    {
        Assert.Throws<UnreachableException>(() => RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            MeshSection,
            (problems, errors) =>
            {
                if (recordAnError) PlantMeshError(problems, errors);
                else problems.Add(MeshProblem);
            }));
    }

    [Fact]
    public void AnArchiveProblemRecordedAfterTheEndOfRunTookThemIsABugThatEndsTheRun()
    {
        Assert.Throws<UnreachableException>(() => RunPlanting(
            SettingsVariants.TouchWithLeftBehindAndMarkerMoves,
            MeshSection,
            (problems, _) => problems.Add(ArchiveProblem)));
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

    /// <summary>The reader notes a mesh's spelling before it can fail on it, so the plant does too.</summary>
    private static void PlantMeshError(AssetProblemLog problems, IUnexpectedErrors errors)
    {
        problems.NoteSpelling(Mesh);
        errors.Add(MeshReadingError());
    }

    private static PartFailure MeshReadingError() =>
        new(MeshReadingParts.MeshTriangles, Failures.DescribeFailure(new InvalidOperationException("planted")), new FailureSubject(Mesh, Name: null, Position: 0));

    private static List<string> SectionsWithLine(RecordingLogSink log, Func<string, bool> isLine) =>
        [.. log.Sections.Where(section => section.Lines.Any(isLine)).Select(section => section.Id)];

    /// <param name="plant">Records a problem or an error into the run's problem log or error channel; called when the sections of <paramref name="afterSection"/> print.</param>
    private static RecordingLogSink RunPlanting(string variant, string afterSection, Action<AssetProblemLog, IUnexpectedErrors> plant)
    {
        PlantingMeshFilesFactory? factory = null;
        var log = new RecordingLogSink
        {
            OnPrint = section =>
            {
                if (section.Id == afterSection) plant((AssetProblemLog)factory!.Files!.Problems, factory.Errors!);
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
        public IMeshFiles Open(ShapeInclusion inclusion, IUnexpectedErrors unexpectedErrors) =>
            new ProblemOnBounds(inner.Open(inclusion, unexpectedErrors), armed, problem);
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
    }

    /// <summary>Opens the run's mesh reader and keeps the reader and the error channel it was given, so a test can record into the run's own channels.</summary>
    private sealed class PlantingMeshFilesFactory(IMeshFilesFactory inner) : IMeshFilesFactory
    {
        public IMeshFiles? Files { get; private set; }

        public IUnexpectedErrors? Errors { get; private set; }

        public IMeshFiles Open(ShapeInclusion inclusion, IUnexpectedErrors unexpectedErrors)
        {
            Errors = unexpectedErrors;
            Files = inner.Open(inclusion, unexpectedErrors);
            return Files;
        }
    }
}
