using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A log-and-reports object over a fake run whose caches serve only the mesh files and the triangle meshes' statistics; asking any other cache fails the test.</summary>
internal sealed class TestReport
{
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");

    private readonly LogAndReports _report;

    public TestReport(IMeshFiles meshFiles, IUnexpectedErrors unexpectedErrors)
    {
        var settings = new StandardSettings { CleanedMod = ModKey.FromNameAndExtension(Target.ToString()) };
        var plugins = TestLoadOrderPlugins.Of(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, true, []), new PluginListing(Patch, true, [])]);
        var ready = Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), plugins, detailedLogAndReports: true, onlyWarnWhenRequiringModLoadsAfterPatch: false, "/out/Patch.esp", 1));
        _report = new LogAndReports(ready.Settings, new ReportFolderPrepared(StepOutcome.SwitchedOff, null), new MeshFilesOnlyCaches(meshFiles), unexpectedErrors);
    }

    /// <summary>A report whose mesh files hold only the given problem log.</summary>
    public static TestReport Over(AssetProblemLog problems) => new(new MeshFilesWithProblems(problems), new UnexpectedErrors());

    /// <summary>A report whose mesh files have no problem.</summary>
    public static TestReport WithoutProblems() => Over(new AssetProblemLog());

    /// <summary>A report whose mesh files have no problem and whose unexpected errors are the given channel.</summary>
    public static TestReport OverErrors(IUnexpectedErrors errors) => new(new MeshFilesWithProblems(new AssetProblemLog()), errors);

    /// <summary>The sections the judge step prints as it ends, in order.</summary>
    public ImmutableArray<LogSection> JudgeSections(ChangedObjectJudgingResult judged, CollectedObjects world, CollectedObjects? oldPlaceWorld) =>
        _report.BuildStepSections(judged, world, oldPlaceWorld, TimeSpan.Zero);

    /// <summary>The sections the selection step prints as it ends, in order; the last takes the unexpected errors recorded since the step before.</summary>
    public ImmutableArray<LogSection> ReplacementSections(Replacements found, CollectedObjects world) =>
        _report.BuildStepSections(found, world, TimeSpan.Zero);

    /// <summary>
    /// The lines of the archive-warnings section, which takes the archive problems met since the last take; without the detailed
    /// log's step-time line of the mesh-reading preparation, which ends the section.
    /// </summary>
    public IReadOnlyList<string> TakeArchiveProblemLines() =>
        [.. _report.BuildArchiveSections(TimeSpan.Zero)[0].Lines.Where(line => !line.StartsWith("Step time: ", StringComparison.Ordinal))];

    /// <summary>The lines of the sections printed after the placed objects were collected, in order.</summary>
    public IReadOnlyList<string> CollectedSectionLines(CollectedObjects world) =>
        [.. _report.BuildStepSections(world, TimeSpan.Zero, TimeSpan.Zero).SelectMany(section => section.Lines)];

    /// <summary>The sections the visible left-behind step prints as it ends, in order.</summary>
    /// <param name="elapsed">The whole step's time as the run measured it.</param>
    public ImmutableArray<LogSection> VisibleLeftBehindSections(
        VisibleLeftBehindResult found, IRemovalDecisions decisions, CollectedObjects world, TimeSpan elapsed = default) =>
        _report.BuildStepSections(found, decisions, world, elapsed);

    private sealed class MeshFilesOnlyCaches(IMeshFiles meshFiles) : ILoadOrderCaches
    {
        public IBaseFacts Bases => null!;

        public IMeshFiles MeshFiles => meshFiles;

        public IBaseObjectShapes Shapes => null!;

        public ITriangleMeshes Triangles => new TriangleStatsOnly();

        public ISurfaceSamples SurfaceSamples => throw NotRead();

        public IBodyMeshBounds BodyBounds => throw NotRead();

        public INpcBodies Bodies => throw NotRead();

        public ITerrainHeights Terrain => throw NotRead();

        public INavmeshes Navmeshes => throw NotRead();

        public BoundsStats CountBounds() => throw NotRead();

        public int CountEffectOnlyMeshes() => throw NotRead();

        public int CountBodyMeshSets() => throw NotRead();

        private static InvalidOperationException NotRead() => new("The problem takes read only the mesh files' problem log.");
    }

    /// <summary>Triangle meshes with nothing indexed, whose statistics the also-remove sections print.</summary>
    private sealed class TriangleStatsOnly : ITriangleMeshes
    {
        public Lease<MeshTriangleTree> Acquire(string meshPath) => throw new InvalidOperationException("The report reads only the statistics of the triangle meshes.");

        public TriangleStoreStats GetStats() => new(Built: 0, TooLarge: 0, Triangles: 0);
    }

    private sealed class MeshFilesWithProblems(AssetProblemLog problems) : IMeshFiles
    {
        public IMeshBounds Bounds => throw NotRead();

        public IMeshProblems Problems => problems;

        public int ArchivesIndexed => throw NotRead();

        public void PrepareReading() => throw NotRead();

        public string NormalizeMeshPath(string givenPath) => throw NotRead();

        public MeshTriangles? ReadTriangles(string meshPath) => throw NotRead();

        public TriangleFailureCounts CountTriangleFailures() => throw NotRead();

        private static InvalidOperationException NotRead() => new("The report reads only the problem log of the mesh files.");
    }
}
