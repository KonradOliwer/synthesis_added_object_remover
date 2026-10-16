using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
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
        var settings = new Settings { TargetPlugin = ModKey.FromNameAndExtension(Target.ToString()), Diagnostics = new DiagnosticsSettings { DetailedLog = true } };
        var plugins = new LoadOrderPlugins(
            new KnownPluginNames([Target, Patch]),
            Patch,
            [new PluginListing(Target, true, []), new PluginListing(Patch, true, [])]);
        var ready = Assert.IsType<RunRequest.Ready>(SettingsValidator.Build(settings, new PluginNameParsingPlugin(), plugins, "/out/Patch.esp", 1));
        _report = new LogAndReports(ready.Settings, new ReportFolderPrepared(StepOutcome.SwitchedOff, null), new MeshFilesOnlyCaches(meshFiles), unexpectedErrors);
    }

    /// <summary>The lines of the archive-warnings section, which takes the archive problems met since the last take.</summary>
    public IReadOnlyList<string> TakeArchiveProblemLines() => [.. _report.BuildArchiveSections()[0].Lines];

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
}
