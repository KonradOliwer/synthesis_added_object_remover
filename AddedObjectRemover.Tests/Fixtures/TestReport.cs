using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A log-and-reports object over a fake run whose caches serve only the mesh files; asking any other cache fails the test.</summary>
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

    /// <summary>The lines of the archive-warnings section, which takes the archive problems met since the last take.</summary>
    public IReadOnlyList<string> TakeArchiveProblemLines() => [.. _report.BuildArchiveSections()[0].Lines];

    /// <summary>The lines of the sections printed after the placed objects were collected, in order.</summary>
    public IReadOnlyList<string> CollectedSectionLines(CollectedObjects world) =>
        [.. _report.BuildStepSections(world, TimeSpan.Zero).SelectMany(section => section.Lines)];

    private sealed class MeshFilesOnlyCaches(IMeshFiles meshFiles) : ILoadOrderCaches
    {
        public IBaseFacts Bases => null!;

        public IMeshFiles MeshFiles => meshFiles;

        public IBaseObjectShapes Shapes => null!;

        public ITriangleMeshes Triangles => throw NotRead();

        public IBodyMeshBounds BodyBounds => throw NotRead();

        public INpcBodies Bodies => throw NotRead();

        public ITerrainHeights Terrain => throw NotRead();

        public INavmeshes Navmeshes => throw NotRead();

        public BoundsStats CountBounds() => throw NotRead();

        public int CountEffectOnlyMeshes() => throw NotRead();

        public int CountBodyMeshSets() => throw NotRead();

        private static InvalidOperationException NotRead() => new("The problem takes read only the mesh files' problem log.");
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
