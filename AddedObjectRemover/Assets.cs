using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>The mesh-reading services of one run, with the problems they meet and the statistics they keep.</summary>
internal sealed class Assets
{
    private readonly ProblemCapture _capture;

    private Assets(Services services, AssetProblemLog problems, IPerfProbe perf, PhaseClock clock)
    {
        Services = services;
        Problems = problems;
        Perf = perf;
        Clock = clock;
        _capture = new ProblemCapture(problems);
    }

    public Services Services { get; }

    public AssetProblemLog Problems { get; }

    public IPerfProbe Perf { get; }

    public PhaseClock Clock { get; }

    public static Assets Create(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IBaseFacts bases)
    {
        var problems = new AssetProblemLog();
        var meshFiles = new MeshFileSource(
            state.DataFolderPath.Path,
            state.GameRelease,
            [.. state.LoadOrder.ListedOrder.Select(listing => listing.ModKey)],
            problems);
        var shapes = new ShapeCatalog(bases, meshFiles, problems);
        var triangles = new TriangleStore(shapes.ReadGeometry);
        var bodyMeasurer = new SkinnedBodyMeasurer(shapes.ReadGeometry);
        var npcBodies = new NpcBodyCache(new NpcBodyResolver(state.LinkCache, shapes, bodyMeasurer));
        return new Assets(
            new Services(shapes, triangles, npcBodies, bases), problems, new PerfProbe(triangles, npcBodies, bodyMeasurer), new PhaseClock());
    }

    /// <param name="detailed">Whether the detailed log is on.</param>
    public ReportContext ReportContext(bool detailed) => new(Services.Bases, Services.Shapes, detailed);

    /// <summary>The problems met since the last call; call it right after each phase that reads meshes.</summary>
    public PhaseProblems TakeProblems() => _capture.Take();

    /// <summary>Every problem of the run so far, whether taken or not.</summary>
    public PhaseProblems AllProblems() => new(Problems.ArchiveProblemsSince(default), Problems.Since(default));
}

/// <summary>Hands out each problem once, in the phase that first met it.</summary>
internal sealed class ProblemCapture(AssetProblemLog problems)
{
    private ProblemMark _takenUpTo;

    public PhaseProblems Take()
    {
        var taken = new PhaseProblems(problems.ArchiveProblemsSince(_takenUpTo), problems.Since(_takenUpTo));
        _takenUpTo = problems.Mark();
        return taken;
    }
}
